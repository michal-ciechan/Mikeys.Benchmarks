using System.Net;
using System.Text;
using JustEat.HttpClientInterception;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Moq;
using Moq.Contrib.HttpClient;
using RichardSzalay.MockHttp;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace HttpClientMocking;

/// <summary>One stubbing library, driven the same way so the benchmarks compare like with like.</summary>
public interface IFake : IDisposable
{
    string Name { get; }
    Uri BaseAddress { get; }
    void Start();                                   // WireMock boots Kestrel here; the in-memory fakes do nothing
    void Register(IReadOnlyList<Endpoint> endpoints);
    void Reset();                                   // drop all registrations, keep the fake alive
    void Configure(IServiceCollection services);    // typed ApiClient through IHttpClientFactory, pointed at the fake
}

public static class Fakes
{
    public static readonly string[] Names = ["Handwritten", "MockHttp", "HttpClientInterception", "MoqContrib", "WireMock"];

    public static IFake Create(string name) => name switch
    {
        "Handwritten" => new HandwrittenFake(),
        "MockHttp" => new MockHttpFake(),
        "HttpClientInterception" => new InterceptionFake(),
        "MoqContrib" => new MoqContribFake(),
        "WireMock" => new WireMockFake(),
        _ => throw new ArgumentException(name),
    };

    internal static void AddClient(IServiceCollection services, Uri baseAddress, Action<IHttpClientBuilder>? configure = null)
    {
        var builder = services.AddHttpClient<ApiClient>(c => c.BaseAddress = baseAddress)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);   // the factory would otherwise recycle (and dispose) our shared handler
        configure?.Invoke(builder);
    }

    internal static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}

// 1. A plain HttpMessageHandler with a dictionary: the floor every library has to beat or justify itself against.
sealed class HandwrittenFake : IFake
{
    sealed class Handler : HttpMessageHandler
    {
        public Dictionary<string, Endpoint> Map = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!Map.TryGetValue(request.Method.Method + " " + request.RequestUri!.PathAndQuery, out var e))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (e.RequestJson is not null && await request.Content!.ReadAsStringAsync(ct) != e.RequestJson)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(e.Status) { Content = Fakes.Json(e.ResponseJson) };
        }
    }

    readonly Handler _handler = new();
    public string Name => "Handwritten";
    public Uri BaseAddress => Scenario.FakeBase;
    public void Start() { }
    public void Register(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints) _handler.Map[e.Method.Method + " " + e.PathAndQuery] = e;
    }
    public void Reset() => _handler.Map = new();
    public void Configure(IServiceCollection services) =>
        Fakes.AddClient(services, BaseAddress, b => b.ConfigurePrimaryHttpMessageHandler(() => _handler));
    public void Dispose() => _handler.Dispose();
}

// 2. RichardSzalay.MockHttp: fluent When(...).WithContent(...).Respond(...), plugged in as the primary handler.
sealed class MockHttpFake : IFake
{
    readonly MockHttpMessageHandler _mock = new();
    public string Name => "MockHttp";
    public Uri BaseAddress => Scenario.FakeBase;
    public void Start() { }
    public void Register(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var request = _mock.When(e.Method, "https://api.test" + e.PathAndQuery);
            if (e.RequestJson is not null) request = request.WithContent(e.RequestJson);
            request.Respond(e.Status, "application/json", e.ResponseJson);
        }
    }
    public void Reset() => _mock.Clear();
    public void Configure(IServiceCollection services) =>
        Fakes.AddClient(services, BaseAddress, b => b.ConfigurePrimaryHttpMessageHandler(() => _mock));
    public void Dispose() => _mock.Dispose();
}

// 3. JustEat.HttpClientInterception: registered as an IHttpMessageHandlerBuilderFilter, so it intercepts every
//    factory-created client without touching the client's own configuration.
sealed class InterceptionFake : IFake
{
    sealed class Filter(HttpClientInterceptorOptions options) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            b => { next(b); b.AdditionalHandlers.Add(options.CreateHttpMessageHandler()); };
    }

    readonly HttpClientInterceptorOptions _options = new() { ThrowOnMissingRegistration = true };
    public string Name => "HttpClientInterception";
    public Uri BaseAddress => Scenario.FakeBase;
    public void Start() { }
    public void Register(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var builder = new HttpRequestInterceptionBuilder()
                .Requests().ForMethod(e.Method).ForHttps().ForHost("api.test").ForPath(e.Path);
            if (e.Params.Length > 0) builder.ForQuery(e.Query);
            if (e.RequestJson is { } expected) builder.ForContent(async c => await c.ReadAsStringAsync() == expected);
            builder.Responds().WithStatus(e.Status).WithMediaType("application/json").WithContent(e.ResponseJson)
                .RegisterWith(_options);
        }
    }
    public void Reset() => _options.Clear();
    public void Configure(IServiceCollection services)
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new Filter(_options));
        Fakes.AddClient(services, BaseAddress);
    }
    public void Dispose() { }
}

// 4. Moq.Contrib.HttpClient: SetupRequest(...).ReturnsResponse(...) on a strict Mock<HttpMessageHandler>.
sealed class MoqContribFake : IFake
{
    readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);
    public string Name => "MoqContrib";
    public Uri BaseAddress => Scenario.FakeBase;
    public void Start() { }
    public void Register(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var url = "https://api.test" + e.PathAndQuery;
            var setup = e.RequestJson is { } expected
                ? _handler.SetupRequest(e.Method, url, async r => await r.Content!.ReadAsStringAsync() == expected)
                : _handler.SetupRequest(e.Method, url);
            setup.ReturnsResponse(e.Status, r => r.Content = Fakes.Json(e.ResponseJson));
        }
    }
    public void Reset() => _handler.Reset();
    public void Configure(IServiceCollection services) =>
        Fakes.AddClient(services, BaseAddress, b => b.ConfigurePrimaryHttpMessageHandler(() => _handler.Object));
    public void Dispose() { }
}

// 5. WireMock.Net: a real Kestrel server on a random loopback port; the client talks to it over a socket.
sealed class WireMockFake : IFake
{
    WireMockServer? _server;
    public string Name => "WireMock";
    public Uri BaseAddress => new(_server!.Url! + "/");
    public void Start() => _server = WireMockServer.Start(new WireMockServerSettings { MaxRequestLogCount = 100 });
    public void Register(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            var request = Request.Create().WithPath(e.Path).UsingMethod(e.Method.Method);
            foreach (var (key, value) in e.Params) request = request.WithParam(key, value);
            if (e.RequestJson is not null) request = request.WithBody(new WireMock.Matchers.ExactMatcher(e.RequestJson));
            _server!.Given(request).RespondWith(Response.Create()
                .WithStatusCode(e.Status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(e.ResponseJson));
        }
    }
    public void Reset() => _server!.Reset();
    public void Configure(IServiceCollection services) => Fakes.AddClient(services, BaseAddress);
    public void Dispose() => _server?.Dispose();
}
