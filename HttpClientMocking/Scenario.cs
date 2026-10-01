using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HttpClientMocking;

public sealed record Item(string Sku, int Qty, decimal Price);

public sealed record Resource(int Id, string Name, string Status, decimal Amount, DateTime Created, List<Item> Items);

/// <summary>One stubbed endpoint: how to match the request and what to answer.</summary>
public sealed record Endpoint(
    HttpMethod Method,
    string Path,
    (string Key, string Value)[] Params,
    Resource? Request,
    string? RequestJson,
    string ResponseJson,
    HttpStatusCode Status,
    int ExpectedId)
{
    public string Query => string.Join("&", Params.Select(p => $"{p.Key}={p.Value}"));
    public string PathAndQuery => Params.Length == 0 ? Path : $"{Path}?{Query}";
    public bool IsPost => Method == HttpMethod.Post;
}

/// <summary>The client under test: a typed HttpClient, registered through IHttpClientFactory.</summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<Resource> GetAsync(string pathAndQuery) =>
        (await http.GetFromJsonAsync<Resource>(pathAndQuery, Scenario.Json))!;

    public async Task<Resource> PostAsync(string path, Resource body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Scenario.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Resource>(Scenario.Json))!;
    }

    public Task<Resource> CallAsync(Endpoint e) => e.IsPost ? PostAsync(e.Path, e.Request!) : GetAsync(e.PathAndQuery);
}

public static class Scenario
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly Uri FakeBase = new("https://api.test/");
    public const int Templates = 20;

    static readonly DateTime Created = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    static Resource Make(int id, string name, int items) => new(
        id, name, "ok", id * 1.25m, Created,
        Enumerable.Range(0, items).Select(k => new Item($"SKU-{id}-{k}", k + 1, 2.5m * (k + 1))).ToList());

    /// <summary>
    /// <paramref name="count"/> endpoints built from 20 templates (12 GET, 8 POST). Past 20 the templates repeat with
    /// different ids, so every path+query (and every POST body) is unique. Responses range from ~1 to ~20 items.
    /// </summary>
    public static IReadOnlyList<Endpoint> Build(int count)
    {
        var list = new List<Endpoint>(count);
        for (var i = 0; i < count; i++) list.Add(Template(i % Templates, i / Templates, i + 1));
        return list;
    }

    static Endpoint Get(string path, int id, int items, params (string, string)[] q) =>
        new(HttpMethod.Get, path, q, null, null, Serialize(Make(id, "get", items)), HttpStatusCode.OK, id);

    static Endpoint Post(string path, int id, int reqItems, int respItems, HttpStatusCode status = HttpStatusCode.Created)
    {
        var request = Make(id, "post", reqItems);
        return new(HttpMethod.Post, path, [], request, Serialize(request), Serialize(Make(id, "created", respItems)), status, id);
    }

    static string Serialize(Resource r) => JsonSerializer.Serialize(r, Json);

    static Endpoint Template(int t, int v, int id) => t switch
    {
        // GET with path parameters
        0 => Get($"/users/{v}", id, 1),
        2 => Get($"/orders/{1000 + v}", id, 3),
        4 => Get($"/products/SKU-{v}", id, 1),
        6 => Get($"/customers/{v}/addresses", id, 4),
        7 => Get($"/invoices/{v}", id, 6),
        // GET with query parameters (list endpoints, larger responses)
        1 => Get("/users", id, 20, ("page", v.ToString()), ("size", "50")),
        3 => Get("/orders", id, 15, ("status", "open"), ("customer", v.ToString()), ("page", "1")),
        5 => Get("/products", id, 12, ("category", "pens"), ("q", "parker"), ("limit", (10 + v).ToString())),
        8 => Get($"/inventory/SKU-{v}", id, 2, ("warehouse", "LHR")),
        9 => Get("/reports/daily", id, 10, ("date", $"2026-10-{v % 28 + 1:D2}"), ("tz", "UTC")),
        10 => Get("/search", id, 20, ("q", "refill"), ("limit", "20"), ("offset", v.ToString())),
        11 => Get(v == 0 ? "/health" : $"/health/{v}", id, 1),
        // POST with a JSON body that must match exactly
        12 => Post($"/users/{v}", id, 1, 1, HttpStatusCode.OK),
        13 => Post($"/orders/{v}", id, 5, 5),
        14 => Post($"/orders/{v}/cancel", id, 1, 1, HttpStatusCode.OK),
        15 => Post($"/products/{v}", id, 3, 3),
        16 => Post($"/payments/{v}", id, 2, 2),
        17 => Post($"/invoices/{v}", id, 8, 8),
        18 => Post($"/inventory/{v}/adjust", id, 4, 1, HttpStatusCode.OK),
        19 => Post($"/customers/{v}", id, 2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };
}
