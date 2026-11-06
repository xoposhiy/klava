using Klava.Core;

namespace Klava.App;

/// <summary>Captures on the selection worker, then opens the result on the tray UI thread.</summary>
internal sealed class TranslationController : IDisposable
{
    private readonly SelectionConverter _selection;
    private readonly Control _dispatch = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private TranslationForm? _form;
    private ITranslationClient? _deepL, _ai;
    private string _deepLKey = "", _aiKey = "", _model = "";
    private int _capturing;
    private int _disposed;
    private int _version;

    public TranslationController(SelectionConverter selection)
    {
        _selection = selection;
        _ = _dispatch.Handle;
    }

    // Runs on the hook thread. The callback does no network, file, or UI work.
    public void Start(TranslationConfig config)
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _capturing, 1) != 0) return;
        var version = Volatile.Read(ref _version);
        _selection.ReadSelection(config.MaxChars, capture =>
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _dispatch.BeginInvoke(() =>
                {
                    if (Volatile.Read(ref _disposed) != 0 || version != _version) return;
                    _form?.Dispose();
                    _form = new TranslationForm(capture.Text ?? "", details => Client(config, details), capture.Error);
                    var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                    _form.Location = new Point(Math.Clamp(Cursor.Position.X + 12, area.Left, Math.Max(area.Left, area.Right - _form.Width)),
                        Math.Clamp(Cursor.Position.Y + 12, area.Top, Math.Max(area.Top, area.Bottom - _form.Height)));
                    _form.Show();
                    _form.Activate();
                });
            }
            catch (InvalidOperationException) { }
            finally { Volatile.Write(ref _capturing, 0); }
        });
    }

    private ITranslationClient Client(TranslationConfig config, bool details)
    {
        var key = (details ? config.OpenaiKey : config.DeeplKey).Trim();
        if (key.Length == 0) key = Environment.GetEnvironmentVariable(details ? "OPENAI_API_KEY" : "DEEPL_API_KEY")?.Trim() ?? "";
        if (key.Length == 0) throw new TranslationException($"Add translation.{(details ? "openaiKey" : "deeplKey")} to {ConfigStore.FilePath}, then Reload config. "
            + (details ? "AI details require an OpenAI API key." : "Translation requires a DeepL API key."));
        if (details)
        {
            if (_ai is null || key != _aiKey || config.Model != _model)
            {
                _aiKey = key;
                _model = config.Model;
                _ai = new CachedTranslationClient(new TranslationClient(_http, key, config.Model));
            }
            return _ai;
        }
        if (_deepL is null || key != _deepLKey)
        {
            _deepLKey = key;
            _deepL = new CachedTranslationClient(new DeepLTranslationClient(_http, key));
        }
        return _deepL;
    }

    public void Reload()
    {
        Interlocked.Increment(ref _version);
        _form?.Dispose();
        _form = null;
        _deepL = _ai = null;
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        _form?.Dispose();
        _dispatch.Dispose();
        _http.Dispose();
    }
}
