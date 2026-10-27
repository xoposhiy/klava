using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Klava.Core;

namespace Klava.App;

/// <summary>The tray icon, its menu, and the engine lifetime.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly string[] ConflictingProcesses =
        ["CarambaSwitcher", "PuntoSwitcher", "punto", "AutoHotkey", "AutoHotkey64", "AutoHotkey32", "AutoHotkeyU64"];

    private readonly NotifyIcon _tray;
    private readonly InputHook _hook;
    private readonly SelectionConverter _selection = new();
    private readonly Icon _activeIcon = MakeIcon(active: true);
    private readonly Icon _pausedIcon = MakeIcon(active: false);
    private readonly object _statsLock = new();
    private readonly Dictionary<int, int> _bounces = [];
    private readonly Dictionary<int, (int Count, long MinGapMs)> _fastRepresses = [];
    private readonly ToolStripMenuItem _enabled, _debounce, _hotkeys, _switcher, _stats, _fastStats, _autostart;
    private volatile Engine _engine;

    public TrayContext()
    {
        _engine = CreateEngine(LoadSettings(out var configError));
        _hook = new InputHook(() => _engine);
        _hook.Start();

        _enabled = Toggle("Enabled", e => e.Enabled, (e, v) => e.Enabled = v);
        _debounce = Toggle("Debounce filter", e => e.DebounceEnabled, (e, v) => e.DebounceEnabled = v);
        _hotkeys = Toggle("Hotkeys", e => e.HotkeysEnabled, (e, v) => e.HotkeysEnabled = v);
        _switcher = Toggle("Layout switcher", e => e.SwitcherEnabled, (e, v) => e.SwitcherEnabled = v);
        _stats = new ToolStripMenuItem { Enabled = false };
        _fastStats = new ToolStripMenuItem { Enabled = false };
        _autostart = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            new ToolStripMenuItem($"Klava {Application.ProductVersion}") { Enabled = false },
            new ToolStripSeparator(),
            _enabled, _debounce, _hotkeys, _switcher,
            new ToolStripSeparator(),
            _stats,
            _fastStats,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open config", null, (_, _) => Open(ConfigStore.FilePath)),
            new ToolStripMenuItem("Reload config", null, (_, _) => Reload()),
            new ToolStripMenuItem("Open log", null, (_, _) => Open(Log.FilePath)),
            _autostart,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => Exit()),
        ]);
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        RefreshMenu();
        Log.Info($"Started Klava {Application.ProductVersion}");

        if (configError is not null) Warn($"Config error, using defaults: {configError}");
        var conflicts = ConflictingProcesses.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
        if (conflicts.Count > 0) Warn($"Close {string.Join(", ", conflicts)}: they conflict with Klava.");
    }

    private Engine CreateEngine(Settings settings)
    {
        var engine = new Engine(settings, new WindowsPlatform(settings.Config.Switcher, _selection), WindowsPlatform.IsCapsLockOn());
        engine.BounceBlocked += OnBounce;
        engine.FastRepressPassed += OnFastRepress;
        engine.Switcher.Switched += () => Log.Info("Layout switched");
        engine.Switcher.Converted += count => Log.Info($"Retyped the last word ({count} keys)");
        if (engine.Tracer is { } tracer)
            tracer.Traced += t => Log.Info($"Trace {Clock.ToLocal(t.DownMs):HH:mm:ss.fff} {t}");
        engine.HotkeySent +=(what, steps) => Log.Info($"Sent for {what}: {string.Join(", ", steps)}");
        return engine;
    }

    private static Settings LoadSettings(out string? error)
    {
        error = null;
        try
        {
            return ConfigStore.Load();
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            Log.Warn($"Config error: {ex.Message}");
            return KlavaConfig.Load(KlavaConfig.DefaultJson);
        }
    }

    private void Reload()
    {
        try
        {
            _engine = CreateEngine(ConfigStore.Load());
            Log.Info("Config reloaded");
            _tray.ShowBalloonTip(2000, "Klava", "Config reloaded.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Config error: {ex.Message}");
            Warn($"Config error, the previous config stays: {ex.Message}");
        }
        RefreshMenu();
    }

    // Runs on the hook thread.
    private void OnBounce(Bounce bounce)
    {
        lock (_statsLock) _bounces[bounce.Vk] = _bounces.GetValueOrDefault(bounce.Vk) + 1;
        Log.Info($"Bounce blocked: {KeyNames.Name(bounce.Vk)}, {bounce.GapMs} ms after release");
    }

    // Runs on the hook thread.
    private void OnFastRepress(FastRepress r)
    {
        lock (_statsLock)
        {
            var (count, min) = _fastRepresses.GetValueOrDefault(r.Vk, (0, long.MaxValue));
            _fastRepresses[r.Vk] = (count + 1, Math.Min(min, r.GapMs));
        }
        Log.Info($"Fast re-press passed: {KeyNames.Name(r.Vk)}, {r.GapMs} ms after release, previous press held {r.PreviousHeldMs} ms");
    }

    private void RefreshMenu()
    {
        var engine = _engine;
        _enabled.Checked = engine.Enabled;
        _debounce.Checked = engine.DebounceEnabled;
        _hotkeys.Checked = engine.HotkeysEnabled;
        _switcher.Checked = engine.SwitcherEnabled;
        _autostart.Checked = ConfigStore.Autostart;

        lock (_statsLock)
        {
            var total = _bounces.Values.Sum();
            var top = _bounces.OrderByDescending(p => p.Value).Take(5).Select(p => $"{KeyNames.Name(p.Key)} {p.Value}");
            _stats.Text = total == 0 ? "Bounces blocked: 0" : $"Bounces blocked: {total} ({string.Join(", ", top)})";

            // The fastest passed re-presses hint at keys that need a chatteringKeys entry.
            var fastest = _fastRepresses.OrderBy(p => p.Value.MinGapMs).Take(5)
                .Select(p => $"{KeyNames.Name(p.Key)} {p.Value.MinGapMs} ms");
            _fastStats.Text = _fastRepresses.Count == 0
                ? "Fastest passed re-presses: none"
                : $"Fastest passed re-presses: {string.Join(", ", fastest)}";
        }
        _tray.Icon = engine.Enabled ? _activeIcon : _pausedIcon;
        _tray.Text = engine.Enabled ? "Klava" : "Klava (paused)";
    }

    private ToolStripMenuItem Toggle(string text, Func<Engine, bool> get, Action<Engine, bool> set) =>
        new(text, null, (_, _) =>
        {
            set(_engine, !get(_engine));
            RefreshMenu();
        });

    private void ToggleAutostart()
    {
        ConfigStore.Autostart = !ConfigStore.Autostart;
        RefreshMenu();
    }

    private void Warn(string message)
    {
        Log.Warn(message);
        _tray.ShowBalloonTip(8000, "Klava", message, ToolTipIcon.Warning);
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot open {path}", ex);
        }
    }

    private void Exit()
    {
        _hook.Dispose();
        _selection.Dispose();
        _tray.Visible = false;
        Log.Info("Stopped");
        Log.Flush();
        ExitThread();
    }

    private static Icon MakeIcon(bool active)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var background = new SolidBrush(active ? Color.FromArgb(0x25, 0x63, 0xEB) : Color.FromArgb(0x80, 0x80, 0x80));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, 12, 12, 180, 90);
            path.AddArc(20, 0, 12, 12, 270, 90);
            path.AddArc(20, 20, 12, 12, 0, 90);
            path.AddArc(0, 20, 12, 12, 90, 90);
            path.CloseFigure();
            g.FillPath(background, path);
            using var font = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("K", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
