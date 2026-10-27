using System.Runtime.InteropServices;
using System.Text;
using Klava.Core;
using static Klava.App.Native;

namespace Klava.App;

/// <summary>
/// Converts the selected text through the clipboard: Ctrl+C, convert, Ctrl+V, restore. The steps
/// wait for other apps, so they run on a dedicated thread, never on the hook thread. The thread
/// owns a message-only window: the clipboard needs an owner window, and other apps send messages
/// to the owner, so the thread keeps handling sent messages while it waits.
/// See "Selected text" in docs/spec.md.
/// </summary>
internal sealed class SelectionConverter : IDisposable
{
    private const uint WM_CONVERT = WM_APP + 1;
    private const int PollMs = 10;
    private const int ModifierTimeoutMs = 100;
    private const int CopyTimeoutMs = 400;
    private const int RestoreDelayMs = 250;
    private const int OpenTries = 10;
    private const long MaxSavedBytes = 32 << 20;
    private const long MaxEditorDataBytes = 1 << 20;

    // In a console, Ctrl+C stops the running program.
    private static readonly string[] ConsoleClasses =
        ["ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "mintty", "VirtualConsoleClass"];

    private static readonly uint[] SavedFormats =
    [
        CF_UNICODETEXT, CF_LOCALE, CF_DIB, CF_HDROP,
        RegisterClipboardFormat("HTML Format"), RegisterClipboardFormat("Rich Text Format"), RegisterClipboardFormat("PNG"),
    ];

    // These formats keep Klava's clipboard data out of the Win+V history and the cloud clipboard.
    private static readonly (uint Format, byte[] Data)[] HistoryFormats =
    [
        (RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), new byte[4]),
        (RegisterClipboardFormat("CanIncludeInClipboardHistory"), new byte[4]),
        (RegisterClipboardFormat("CanUploadToCloudClipboard"), new byte[4]),
    ];

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private nint _window;
    private int _busy;
    private Request? _request;

    private sealed record Request(IPlatform Platform, nint From, nint To, int MaxChars);

    private sealed record Copied(string? Text, List<string> Formats, string? EditorData);

    public SelectionConverter()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Klava selection" };
        _thread.Start();
        _started.Wait();
    }

    /// <summary>Starts a conversion and returns at once. Runs on the hook thread.</summary>
    public void Start(IPlatform platform, nint from, nint to, int maxChars)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            Log.Info("Selected text: skipped, a conversion runs");
            return;
        }
        Volatile.Write(ref _request, new Request(platform, from, to, maxChars));
        if (PostThreadMessage(_threadId, WM_CONVERT, 0, 0)) return;
        Log.Warn($"Selected text: error, PostThreadMessage failed, error {Marshal.GetLastWin32Error()}");
        Volatile.Write(ref _busy, 0);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PM_NOREMOVE); // create the message queue before anyone posts to it
        _window = CreateWindowEx(0, "Message", null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        if (_window == 0) Log.Warn($"Selected text: CreateWindowEx failed, error {Marshal.GetLastWin32Error()}");
        _started.Set();

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg is { hwnd: 0, message: WM_CONVERT })
            {
                RunRequest();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_window != 0) DestroyWindow(_window);
    }

    private void RunRequest()
    {
        try
        {
            if (_window == 0) Fail("no clipboard window");
            else Convert(Volatile.Read(ref _request)!);
        }
        catch (Exception ex)
        {
            Log.Error("Selected text", ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void Convert(Request request)
    {
        if (IsConsole(GetForegroundWindow()))
        {
            Done("skipped, console window");
            return;
        }
        // The hook sees the Shift release before Windows updates the key state.
        if (!WaitForModifiersUp())
        {
            Done("skipped, a modifier is down");
            return;
        }

        var saved = Save();
        if (saved is null)
        {
            Fail("cannot open the clipboard");
            return;
        }

        var sequence = GetClipboardSequenceNumber();
        SendChord('C');
        if (!WaitForChange(sequence))
        {
            Done("nothing selected");
            return;
        }

        var copied = ReadCopied();
        if (copied is null)
        {
            Restore(saved);
            Fail("cannot open the clipboard");
            return;
        }
        var reason = SelectionText.SkipReason(copied.Text, copied.Formats, copied.EditorData, request.MaxChars);
        if (reason is not null)
        {
            Restore(saved);
            Done($"skipped, {reason} ({copied.Text?.Length ?? 0} characters)");
            return;
        }

        var text = copied.Text!;
        var result = SelectionText.Convert(text, request.From, request.To, request.Platform);
        if (result == text)
        {
            Restore(saved);
            Done($"skipped, no character changes ({text.Length} characters)");
            return;
        }

        if (!Write([(CF_UNICODETEXT, Encoding.Unicode.GetBytes(result + "\0"))]))
        {
            Restore(saved);
            Fail("cannot write the clipboard");
            return;
        }
        var own = GetClipboardSequenceNumber();
        SendChord('V');

        // A restore that comes too early makes the app paste the old content.
        Wait(RestoreDelayMs);
        if (GetClipboardSequenceNumber() != own)
        {
            Done($"converted ({text.Length} characters), the clipboard has new data and is not restored");
            return;
        }
        if (!Restore(saved))
        {
            Fail($"converted ({text.Length} characters), cannot restore the clipboard");
            return;
        }
        Done($"converted ({text.Length} characters)");
    }

    private static void Done(string result) => Log.Info($"Selected text: {result}");

    private static void Fail(string result) => Log.Warn($"Selected text: error, {result}");

    private static bool IsConsole(nint window)
    {
        var buffer = new char[256];
        var length = GetClassName(window, buffer, buffer.Length);
        return length > 0 && ConsoleClasses.Contains(new string(buffer, 0, length));
    }

    private static bool WaitForModifiersUp()
    {
        for (var waited = 0; ; waited += PollMs)
        {
            if (!Vk.Modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)) return true;
            if (waited >= ModifierTimeoutMs) return false;
            Wait(PollMs);
        }
    }

    private static bool WaitForChange(uint sequence)
    {
        for (var waited = 0; waited < CopyTimeoutMs; waited += PollMs)
        {
            Wait(PollMs);
            if (GetClipboardSequenceNumber() != sequence) return true;
        }
        return false;
    }

    private static void SendChord(char key) => WindowsPlatform.SendSteps(
    [
        new KeyStroke(Vk.LCtrl, false), new KeyStroke(key, false),
        new KeyStroke(key, true), new KeyStroke(Vk.LCtrl, true),
    ]);

    /// <summary>Waits and handles the messages that other apps send to the clipboard window.</summary>
    private static void Wait(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (true)
        {
            // Handles sent messages only; posted messages, WM_QUIT among them, stay in the queue.
            PeekMessage(out _, 0, 0, 0, PM_NOREMOVE | PM_QS_SENDMESSAGE);
            var left = end - Environment.TickCount64;
            if (left <= 0) return;
            MsgWaitForMultipleObjects(0, null, false, (uint)left, QS_SENDMESSAGE);
        }
    }

    private bool Open()
    {
        for (var i = 0; i < OpenTries; i++)
        {
            if (OpenClipboard(_window)) return true;
            Wait(PollMs);
        }
        return false;
    }

    /// <summary>Reads the standard formats in their original order. Never reads private formats.</summary>
    private List<(uint Format, byte[] Data)>? Save()
    {
        if (!Open()) return null;
        try
        {
            var saved = new List<(uint, byte[])>();
            long total = 0;
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (!SavedFormats.Contains(format) || ReadData(format, MaxSavedBytes - total) is not { } data) continue;
                saved.Add((format, data));
                total += data.Length;
            }
            return saved;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private Copied? ReadCopied()
    {
        if (!Open()) return null;
        try
        {
            string? text = null;
            var formats = new List<string>();
            var editorData = new StringBuilder();
            var name = new char[256];
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (format == CF_UNICODETEXT && ReadData(format, MaxSavedBytes) is { } textData)
                {
                    text = Encoding.Unicode.GetString(textData);
                    var end = text.IndexOf('\0');
                    if (end >= 0) text = text[..end];
                }

                var length = GetClipboardFormatName(format, name, name.Length);
                if (length == 0) continue;
                var formatName = new string(name, 0, length);
                formats.Add(formatName);
                if (SelectionText.EditorDataFormats.Contains(formatName) && ReadData(format, MaxEditorDataBytes) is { } data)
                {
                    // The encoding of this data is not documented, so check both.
                    editorData.Append(Encoding.Unicode.GetString(data)).Append('\n').Append(Encoding.UTF8.GetString(data));
                }
            }
            return new Copied(text, formats, editorData.Length > 0 ? editorData.ToString() : null);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Returns a copy of the format data, or null when it is missing or longer than the limit.</summary>
    private static byte[]? ReadData(uint format, long maxBytes)
    {
        var handle = GetClipboardData(format);
        if (handle == 0) return null;
        var size = (long)GlobalSize(handle);
        if (size > maxBytes) return null;
        var pointer = GlobalLock(handle);
        if (pointer == 0) return null;
        try
        {
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private bool Restore(List<(uint Format, byte[] Data)> saved) => Write(saved);

    /// <summary>Replaces the clipboard content with the formats and the history formats.</summary>
    private bool Write(IEnumerable<(uint Format, byte[] Data)> formats)
    {
        if (!Open()) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            var ok = true;
            foreach (var (format, data) in formats.Concat(HistoryFormats)) ok &= SetData(format, data);
            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool SetData(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
        if (handle == 0) return false;
        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            GlobalFree(handle);
            return false;
        }
        Marshal.Copy(data, 0, pointer, data.Length);
        GlobalUnlock(handle);
        // After a successful call the system owns the memory.
        if (SetClipboardData(format, handle) != 0) return true;
        GlobalFree(handle);
        return false;
    }
}
