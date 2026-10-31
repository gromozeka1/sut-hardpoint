using System.Net;
using System.Text;
using System.Text.Json;

namespace WebApp.Tests.Fakes;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> double: records every outgoing request
/// (method, URI and body) and answers with whatever the responder returns.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        return await _responder(request, cancellationToken);
    }
}

/// <summary>
/// Builders for the canned ProductAPI responses used across the test suite.
/// </summary>
public static class StubResponses
{
    public static readonly Uri BaseAddress = new("http://productapi.test/");

    private static readonly JsonSerializerOptions _camelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static HttpResponseMessage Json(
        object? payload,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _camelCase),
                Encoding.UTF8,
                "application/json")
        };

    public static HttpResponseMessage Text(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}