using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using WebApp.Models;

namespace WebApp.Tests.Integration;

/// <summary>
/// A product as serialized on the wire by the Product API: camelCase JSON with the
/// <see cref="ProductType"/> enum as its numeric value (the API configures no enum converter).
/// </summary>
public sealed class StubProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int ProductType { get; set; }

    public int Price { get; set; }

    public int Stock { get; set; }
}

/// <summary>
/// Stateful <see cref="HttpMessageHandler"/> double for the whole Product API surface
/// (list/search/type filter, get, create, update, delete) used as the primary handler of
/// WebApp's typed <c>ProductApiClient</c> inside <see cref="IntegrationTestBase"/>.
/// No socket is opened: requests stay inside the test host.
/// </summary>
public sealed class ProductApiStub : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<StubProduct> _products = new();
    private int _nextId = 1;

    /// <summary>When true every request fails with <see cref="HttpRequestException"/>, as if the API were down.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Every request received, formatted as "VERB path?query".</summary>
    public List<string> Requests { get; } = new();

    public IReadOnlyList<StubProduct> Products => _products;

    public StubProduct Add(
        string name,
        int price = 99,
        int stock = 5,
        string? description = null,
        ProductType productType = ProductType.CPU)
    {
        StubProduct product = new()
        {
            Id = _nextId++,
            Name = name,
            Price = price,
            Stock = stock,
            Description = description,
            ProductType = (int)productType
        };
        _products.Add(product);
        return product;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Unreachable)
        {
            throw new HttpRequestException("No connection could be made because the Product API is down.");
        }

        Uri uri = request.RequestUri!;
        string path = uri.AbsolutePath.TrimEnd('/');
        Requests.Add($"{request.Method} {path}{uri.Query}");

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] != "api" || segments[1] != "products")
        {
            return Json(new { }, HttpStatusCode.NotFound);
        }

        return request.Method.Method switch
        {
            "GET" when segments.Length == 2 => List(uri.Query),
            "GET" => Find(segments[2]),
            "POST" => await CreateAsync(request, cancellationToken),
            "PUT" when segments.Length == 3 => await UpdateAsync(request, segments[2], cancellationToken),
            "DELETE" when segments.Length == 3 => Delete(segments[2]),
            _ => Json(new { }, HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage List(string queryString)
    {
        Dictionary<string, string> query = ParseQuery(queryString);
        IEnumerable<StubProduct> result = _products;

        if (query.TryGetValue("search", out string? search) && !string.IsNullOrWhiteSpace(search))
        {
            result = result.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (query.TryGetValue("type", out string? type) && !string.IsNullOrWhiteSpace(type))
        {
            result = result.Where(p => MatchesType(p.ProductType, type));
        }

        return Json(result.ToList());
    }

    private HttpResponseMessage Find(string idText)
        => TryGet(idText, out StubProduct? product)
            ? Json(product)
            : Json(new { }, HttpStatusCode.NotFound);

    private async Task<HttpResponseMessage> CreateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        StubProduct? incoming = await ReadAsync(request, cancellationToken);
        if (incoming is null)
        {
            return Json(new { }, HttpStatusCode.BadRequest);
        }

        if (IsDuplicateName(incoming.Name, excludeId: null))
        {
            return Conflict(incoming.Name);
        }

        incoming.Id = _nextId++;
        _products.Add(incoming);
        return Json(incoming, HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> UpdateAsync(
        HttpRequestMessage request,
        string idText,
        CancellationToken cancellationToken)
    {
        if (!TryGet(idText, out StubProduct? existing))
        {
            return Json(new { }, HttpStatusCode.NotFound);
        }

        StubProduct? incoming = await ReadAsync(request, cancellationToken);
        if (incoming is null)
        {
            return Json(new { }, HttpStatusCode.BadRequest);
        }

        if (IsDuplicateName(incoming.Name, excludeId: existing.Id))
        {
            return Conflict(incoming.Name);
        }

        existing.Name = incoming.Name;
        existing.Description = incoming.Description;
        existing.ProductType = incoming.ProductType;
        existing.Price = incoming.Price;
        existing.Stock = incoming.Stock;
        return Json(existing);
    }

    private HttpResponseMessage Delete(string idText)
    {
        if (!TryGet(idText, out StubProduct? existing))
        {
            return Json(new { }, HttpStatusCode.NotFound);
        }

        _products.Remove(existing);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private bool TryGet(string idText, [NotNullWhen(true)] out StubProduct? product)
    {
        product = null;
        if (!int.TryParse(idText, out int id))
        {
            return false;
        }

        product = _products.FirstOrDefault(p => p.Id == id);
        return product is not null;
    }

    private bool IsDuplicateName(string name, int? excludeId)
        => _products.Any(p => p.Id != excludeId
            && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static HttpResponseMessage Conflict(string name)
        => Json(new { detail = $"A product named '{name}' already exists." }, HttpStatusCode.Conflict);

    private static async Task<StubProduct?> ReadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return null;
        }

        string body = await request.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<StubProduct>(body, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool MatchesType(int productType, string type)
    {
        if (int.TryParse(type, out int numeric))
        {
            return numeric == productType;
        }

        return string.Equals(type, ((ProductType)productType).ToString(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                type,
                Enum.GetName(typeof(ProductType), productType),
                StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    private static HttpResponseMessage Json(object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _json),
                Encoding.UTF8,
                "application/json")
        };
}