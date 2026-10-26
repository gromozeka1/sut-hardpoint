using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Services;

namespace WebApp.Tests.Integration;

/// <summary>
/// Boots the real WebApp pipeline (routing, model binding, antiforgery, Razor view
/// rendering, layout) on an in-memory TestServer via <see cref="WebApplicationFactory{Program}"/>,
/// with the typed <see cref="ProductApiClient"/> primary handler swapped for
/// <see cref="ProductApiStub"/> so no socket is ever opened. Every test gets its own
/// factory and its own isolated stub state.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    protected IntegrationTestBase()
    {
        Api = new ProductApiStub();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHttpClient<ProductApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Api)));

        Client = _factory.CreateClient();
        RedirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>The in-memory Product API backing this test's requests.</summary>
    protected ProductApiStub Api { get; }

    /// <summary>TestServer-backed client (follows redirects, keeps cookies like a browser).</summary>
    protected HttpClient Client { get; }

    /// <summary>Same pipeline, but redirect responses are surfaced instead of followed.</summary>
    protected HttpClient RedirectClient { get; }

    /// <summary>GETs a URL and asserts a 200, returning the rendered HTML.</summary>
    protected async Task<string> GetHtmlAsync(string url)
    {
        using HttpResponseMessage response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs a URL and asserts the response status, returning the body.</summary>
    protected async Task<string> GetStringAsync(string url, HttpStatusCode expectedStatus)
    {
        using HttpResponseMessage response = await Client.GetAsync(url);
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>POSTs an application/x-www-form-urlencoded form (with antiforgery token) and returns the final response.</summary>
    protected Task<HttpResponseMessage> PostFormAsync(string url, IDictionary<string, string> form)
        => PostFormAsync(Client, url, form);

    /// <summary>POSTs a form through a specific client (use the same one the form was GET-ed from so the antiforgery cookie matches).</summary>
    protected async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        IDictionary<string, string> form)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        return await client.SendAsync(request);
    }

    /// <summary>Returns the value of the rendered &lt;input&gt; with the given name, attribute-order independent, or null.</summary>
    protected static string? FindInputValue(string html, string name)
    {
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>"))
        {
            if (!Regex.IsMatch(input.Value, $"name=\"{Regex.Escape(name)}\""))
            {
                continue;
            }

            Match value = Regex.Match(input.Value, "value=\"([^\"]*)\"");
            return value.Success ? value.Groups[1].Value : null;
        }

        return null;
    }

    /// <summary>Returns the value of the currently selected &lt;option&gt; of the first &lt;select&gt;, or null.</summary>
    protected static string? FindSelectedOptionValue(string html)
    {
        foreach (Match option in Regex.Matches(html, "<option\\b[^>]*>"))
        {
            if (!Regex.IsMatch(option.Value, "\\bselected\\b"))
            {
                continue;
            }

            Match value = Regex.Match(option.Value, "value=\"([^\"]*)\"");
            if (value.Success)
            {
                return value.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the hidden antiforgery token a GET of the form page embeds, exactly as a
    /// browser would echo it back on submit.
    /// </summary>
    protected static string ExtractAntiForgeryToken(string html)
    {
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>"))
        {
            if (!input.Value.Contains("__RequestVerificationToken", StringComparison.Ordinal))
            {
                continue;
            }

            Match value = Regex.Match(input.Value, "value=\"([^\"]+)\"");
            if (value.Success)
            {
                return value.Groups[1].Value;
            }
        }

        throw new InvalidOperationException("No antiforgery token was found in the rendered form.");
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
