using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace HttpClientMocking;

public sealed class Cfg : ManualConfig
{
    public Cfg() => AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(12));
}

/// <summary>Cost of getting a test ready: create the fake, register N endpoints, build the container, resolve the client.</summary>
[Config(typeof(Cfg)), MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class SetupBenchmarks
{
    [Params("Handwritten", "MockHttp", "HttpClientInterception", "MoqContrib", "WireMock")]
    public string Fake = "";

    [Params(20, 200)]
    public int Endpoints;

    IReadOnlyList<Endpoint> _endpoints = [];
    IFake _warm = null!;

    [GlobalSetup]
    public void Setup()
    {
        _endpoints = Scenario.Build(Endpoints);
        _warm = Fakes.Create(Fake);
        _warm.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => _warm.Dispose();

    /// <summary>Everything a test pays: fake (incl. WireMock's server boot), registrations, DI container, client, teardown.</summary>
    [Benchmark]
    public ApiClient SetupFull()
    {
        using var fake = Fakes.Create(Fake);
        fake.Start();
        fake.Register(_endpoints);
        var services = new ServiceCollection();
        fake.Configure(services);
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ApiClient>();
    }

    /// <summary>Just the registrations, on an already-running fake (what a shared fixture pays per test).</summary>
    [Benchmark]
    public void RegisterOnly()
    {
        _warm.Reset();
        _warm.Register(_endpoints);
    }
}

/// <summary>Cost per request once everything is warm, through IHttpClientFactory, including JSON (de)serialization.</summary>
[Config(typeof(Cfg)), MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class RequestBenchmarks
{
    [Params("Handwritten", "MockHttp", "HttpClientInterception", "MoqContrib", "WireMock")]
    public string Fake = "";

    [Params(20, 200)]
    public int Endpoints;

    IFake _fake = null!;
    ServiceProvider _sp = null!;
    ApiClient _client = null!;
    IReadOnlyList<Endpoint> _endpoints = [];
    Endpoint _get = null!, _post = null!;

    [GlobalSetup]
    public void Setup()
    {
        _endpoints = Scenario.Build(Endpoints);
        _fake = Fakes.Create(Fake);
        _fake.Start();
        _fake.Register(_endpoints);
        var services = new ServiceCollection();
        _fake.Configure(services);
        _sp = services.BuildServiceProvider();
        _client = _sp.GetRequiredService<ApiClient>();
        // a GET and a POST from the second half of the registration list (the unlucky end for linear matchers)
        _get = _endpoints.Skip(Endpoints / 2).First(e => !e.IsPost && e.Params.Length > 0);
        _post = _endpoints.Skip(Endpoints / 2).First(e => e.IsPost);
    }

    [GlobalCleanup]
    public void Cleanup() { _sp.Dispose(); _fake.Dispose(); }

    [Benchmark] public Task<Resource> GetWithQuery() => _client.CallAsync(_get);

    [Benchmark] public Task<Resource> PostJsonBody() => _client.CallAsync(_post);

    /// <summary>Every registered endpoint once, one after another (divide by Endpoints for the per-call average).</summary>
    [Benchmark]
    public async Task AllEndpointsSequential()
    {
        foreach (var e in _endpoints) await _client.CallAsync(e);
    }

    /// <summary>Every registered endpoint once, all in flight together.</summary>
    [Benchmark]
    public Task AllEndpointsParallel() => Task.WhenAll(_endpoints.Select(_client.CallAsync));
}
