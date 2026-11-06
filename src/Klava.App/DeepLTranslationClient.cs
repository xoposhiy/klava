using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Klava.Core;

namespace Klava.App;

internal sealed class DeepLTranslationClient(HttpClient http, string key) : ITranslationClient
{
    internal static string Endpoint(string key) => key.EndsWith(":fx", StringComparison.Ordinal)
        ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate";

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation)
    {
        var actualTarget = target == "Auto" ? TranslationText.DefaultTarget(source) : target;
        var result = await Request(text, source, actualTarget, cancellation).ConfigureAwait(false);
        if (target == "Auto" && result.Source == actualTarget)
            result = await Request(text, result.Source, TranslationText.DefaultTarget(result.Source), cancellation).ConfigureAwait(false);
        return result;
    }

    private async Task<TranslationResult> Request(string text, string source, string target, CancellationToken cancellation)
    {
        var payload = new Dictionary<string, object> { ["text"] = new[] { text }, ["target_lang"] = target };
        if (source != "Auto") payload["source_lang"] = source;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(key));
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new TranslationException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "DeepL rejected the API key. Check translation.deeplKey.",
            (HttpStatusCode)456 => "The DeepL translation quota is used up.",
            HttpStatusCode.TooManyRequests or (HttpStatusCode)529 => "DeepL is busy. Try again later.",
            HttpStatusCode.RequestEntityTooLarge => "The selection is too long for DeepL.",
            >= HttpStatusCode.InternalServerError => "DeepL is temporarily unavailable. Try again later.",
            _ => $"DeepL returned HTTP {(int)response.StatusCode}. Check the selected languages or try again later.",
        });
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 1 << 20) throw new TranslationException("The translation response is too large.");
            buffer.Write(chunk, 0, read);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.TryGetProperty("translations", out var translations)
                && translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0)
            {
                var translation = translations[0];
                var detected = source == "Auto" && translation.TryGetProperty("detected_source_language", out var language)
                    ? language.GetString() : source;
                if (!TranslationText.Languages.Contains(detected))
                    throw new TranslationException("The detected language is not EN, RU, or DE. Choose the source language.");
                if (translation.TryGetProperty("text", out var value) && value.GetString() is { Length: > 0 } result)
                    return new(detected!, target, "", [new(result, "", "", "", "")], "");
            }
        }
        catch (JsonException) { }
        throw new TranslationException("DeepL returned an invalid translation. Try again.");
    }
}

/// <summary>Keeps a small session cache. Errors and cancelled requests are never cached.</summary>
internal sealed class CachedTranslationClient(ITranslationClient inner) : ITranslationClient
{
    private readonly Dictionary<(string Text, string Source, string Target), TranslationResult> _cache = [];

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = (text, source, target);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var result = await inner.TranslateAsync(text, source, target, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (_cache.Count >= 100) _cache.Remove(_cache.Keys.First());
        _cache[key] = result;
        return result;
    }
}
