using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

internal sealed record TranslationHttpRequest(Uri Uri, string? Authorization, JsonElement Body);

internal sealed class TranslationHttpHandler(Func<int, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
{
    public List<TranslationHttpRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Requests.Add(new(request.RequestUri!, request.Headers.Authorization?.ToString(), JsonDocument.Parse(body).RootElement.Clone()));
        var response = reply(Requests.Count);
        return new(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
    }
}

internal sealed class PendingTranslationClient : ITranslationClient
{
    public sealed record Request(string Text, string Source, string Target, CancellationToken Cancellation,
        TaskCompletionSource<TranslationResult> Completion);

    public List<Request> Requests { get; } = [];

    public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<TranslationResult>();
        Requests.Add(new(text, source, target, cancellation, completion));
        return completion.Task;
    }
}

internal static class TranslationTestKit
{
    public static TranslationResult Result(string text = "translated", string source = "EN", string target = "RU") =>
        new(source, target, "", [new(text, "", "", "", "")], "");

    public static string Response(TranslationResult result) => JsonSerializer.Serialize(new
    {
        status = "completed",
        output = new[] { new { type = "message", content = new[] { new { type = "output_text", text =
            JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) } } } },
    });

    public static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(done(), "The translation operation did not finish.");
    }
}
