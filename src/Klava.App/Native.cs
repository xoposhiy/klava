using System.Runtime.InteropServices;

namespace Klava.App;

internal static class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_APP = 0x8000;
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E;
    public const uint LLKHF_EXTENDED = 0x01, LLKHF_LOWER_IL_INJECTED = 0x02, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x01, KEYEVENTF_KEYUP = 0x02, KEYEVENTF_UNICODE = 0x04;
    public const uint MAPVK_VK_TO_VSC = 0, MAPVK_VSC_TO_VK = 1, MAPVK_VK_TO_VSC_EX = 4;
    public const uint CF_DIB = 8, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_LOCALE = 16;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint QS_SENDMESSAGE = 0x0040, PM_NOREMOVE = 0x0000, PM_QS_SENDMESSAGE = QS_SENDMESSAGE << 16;
    public const nint HWND_MESSAGE = -3;
    public const int VK_SHIFT = 0x10, VK_CAPITAL = 0x14;

    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    public delegate void TimerProc(nint hwnd, uint msg, nuint id, uint time);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, TimerProc lpTimerFunc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(nint hWnd, nuint uIDEvent);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // The union must include MOUSEINPUT, the largest member, so that sizeof(INPUT) is right.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPArray)] char[] pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    public static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScanEx(char ch, nint dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjects(uint nCount, nint[]? pHandles, bool bWaitAll, uint dwMilliseconds, uint dwWakeMask);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    public static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClipboardFormatName(uint format, [Out] char[] lpszFormatName, int cchMaxCount);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalFree(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern nuint GlobalSize(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);
}
