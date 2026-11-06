using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Klava.Core;

namespace Klava.App;

internal interface ITranslationClient
{
    Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation);
}

/// <summary>OpenAI Responses transport. Neither service errors nor response bodies go into logs.</summary>
internal sealed class TranslationClient(HttpClient http, string key, string model) : ITranslationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object", "additionalProperties": false,
          "properties": {
            "source": {"type": "string", "enum": ["EN", "RU", "DE"]},
            "target": {"type": "string", "enum": ["EN", "RU", "DE"]},
            "headword": {"type": "string"},
            "entries": {"type": "array", "items": {
              "type": "object", "additionalProperties": false,
              "properties": {
                "text": {"type": "string"}, "partOfSpeech": {"type": "string"},
                "meaning": {"type": "string"}, "example": {"type": "string"},
                "exampleTranslation": {"type": "string"}
              },
              "required": ["text", "partOfSpeech", "meaning", "example", "exampleTranslation"]
            }},
            "note": {"type": "string"}
          },
          "required": ["source", "target", "headword", "entries", "note"]
        }
        """).RootElement.Clone();

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new
        {
            model,
            reasoning = new { effort = "none" },
            store = false,
            max_output_tokens = 4000,
            instructions = TranslationPrompt.Instructions(TranslationText.IsWord(text), source, target),
            input = new[] { new { role = "user", content = text } },
            text = new { format = new { type = "json_schema", name = "translation", strict = true, schema = Schema } },
        });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new TranslationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "OpenAI rejected the API key. Check translation.openaiKey.",
            HttpStatusCode.TooManyRequests => "OpenAI quota or rate limit reached. Check API billing or try again later.",
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "OpenAI rejected the request. Check translation.model and API access.",
            >= HttpStatusCode.InternalServerError => "OpenAI is temporarily unavailable. Try again later.",
            _ => $"OpenAI returned HTTP {(int)response.StatusCode}. Try again later.",
        });

        await using var body = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 1 << 20) throw new TranslationException("The translation response is too large.");
            buffer.Write(chunk, 0, read);
        }
        try { return Parse(buffer.ToArray()); }
        catch (JsonException) { throw new TranslationException("OpenAI returned an invalid translation. Try again."); }
    }

    internal static TranslationResult Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new TranslationException("The translation did not finish. Select less text or try again.");
        var text = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "message" ||
                    !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (!part.TryGetProperty("type", out var partType)) continue;
                    if (partType.GetString() == "refusal") throw new TranslationException("OpenAI declined to translate this selection.");
                    if (partType.GetString() == "output_text" && part.TryGetProperty("text", out var value)) text.Append(value.GetString());
                }
            }
        if (text.Length == 0) throw new TranslationException("OpenAI returned no translation. Try again.");
        var result = JsonSerializer.Deserialize<TranslationResult>(text.ToString(), JsonOptions);
        if (result is null || !TranslationText.Languages.Contains(result.Source) || !TranslationText.Languages.Contains(result.Target)
            || result.Headword is null || result.Note is null || result.Entries is null || result.Entries.Length > 100
            || result.Entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.Text) || e.PartOfSpeech is null
                || e.Meaning is null || e.Example is null || e.ExampleTranslation is null)
            || result.Entries.Length == 0 && string.IsNullOrWhiteSpace(result.Note))
            throw new TranslationException("OpenAI returned an invalid translation. Try again.");
        return result;
    }
}

internal sealed class TranslationException(string message) : Exception(message);
