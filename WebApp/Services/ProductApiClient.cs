using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using WebApp.Models;

namespace WebApp.Services;

public class ProductApiClient
{
    private const string _productsEndpoint = "api/products";
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public ProductApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ProductViewModel>> GetAllAsync(
        string? search = null,
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(_productsEndpoint, search, type);
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        _ = response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ProductViewModel>>(cancellationToken: cancellationToken)
            ?? throw new JsonException("The Product API returned an empty product list response.");
    }

    public async Task<ProductViewModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"{_productsEndpoint}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        _ = response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductViewModel>(cancellationToken: cancellationToken)
            ?? throw new JsonException("The Product API returned an empty product response.");
    }

    public async Task<(bool Success, string? ConflictMessage)> CreateAsync(
        ProductViewModel product,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_productsEndpoint, product, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            string? detail = await ReadConflictDetailAsync(response);
            return (false, detail);
        }

        _ = response.EnsureSuccessStatusCode();
        return (true, null);
    }

    public async Task<(bool Success, string? ConflictMessage)> UpdateAsync(
        ProductViewModel product,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"{_productsEndpoint}/{product.Id}",
            product,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, null);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            string? detail = await ReadConflictDetailAsync(response);
            return (false, detail);
        }

        _ = response.EnsureSuccessStatusCode();
        return (true, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"{_productsEndpoint}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        _ = response.EnsureSuccessStatusCode();
        return true;
    }

    private static string BuildUrl(string endpoint, string? search, string? type)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query["search"] = search;
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query["type"] = type;
        }

        string queryString = query.ToString()!;
        return string.IsNullOrEmpty(queryString) ? endpoint : $"{endpoint}?{queryString}";
    }

    private static async Task<string?> ReadConflictDetailAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
            return problem?.Detail;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}