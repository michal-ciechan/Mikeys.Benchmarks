# Stubbing HTTP in .NET tests: five approaches, 20 and 200 endpoints

How fast are the common ways of faking HTTP calls in a .NET test when the client under test is a typed
`HttpClient` from `IHttpClientFactory`, and the stubs are a realistic mix of GETs and POSTs with path parameters,
query parameters and JSON bodies? Measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) on .NET 10.

**Result:** for in-memory stubbing, a hand-written handler, `JustEat.HttpClientInterception` and `MockHttp` are all
under 0.5 ms to set up at 20 endpoints and answer in tens of microseconds. They separate at 200 endpoints, where
`MockHttp` slows down about 4× per request. `Moq.Contrib.HttpClient` is quick per request but pays about **16-20 ms per
registered stub** (0.33 s for 20, 3.9 s for 200). `WireMock.Net` is a real HTTP server, and it costs about 1.5 s
in a fresh process and 0.3-2 ms per request, because it is doing real network and server work.

## Contents

- [What was compared](#what-was-compared)
- [The scenario](#the-scenario)
- [Results](#results)
- [Findings](#findings)
- [Caveats](#caveats)
- [Running it](#running-it)

## What was compared

| Variant | How it plugs into `IHttpClientFactory` | Version |
|---|---|---|
| **Handwritten** | ~15-line `HttpMessageHandler` over a `Dictionary<string, Endpoint>`, `ConfigurePrimaryHttpMessageHandler` | n/a |
| **MockHttp** | `MockHttpMessageHandler` with `When(...).WithContent(...).Respond(...)`, `ConfigurePrimaryHttpMessageHandler` | RichardSzalay.MockHttp 7.1.0 |
| **HttpClientInterception** | `HttpRequestInterceptionBuilder` registrations + an `IHttpMessageHandlerBuilderFilter` that adds the interceptor to every factory client | JustEat.HttpClientInterception 5.1.4 |
| **MoqContrib** | strict `Mock<HttpMessageHandler>`, `SetupRequest(...).ReturnsResponse(...)`, `ConfigurePrimaryHttpMessageHandler` | Moq.Contrib.HttpClient 1.4.0 + Moq 4.20.72 |
| **WireMock** | real in-process Kestrel server on a random port; the client's `BaseAddress` points at it | WireMock.Net 2.18.0 |

All five are driven through the same interface ([Fakes.cs](Fakes.cs)) and the same `ApiClient`
([Scenario.cs](Scenario.cs)), so every request goes through the factory, a typed client, `System.Text.Json`
serialization and deserialization, and the stub's matcher. The Handwritten row is therefore the floor: it is what
`HttpClient` + JSON + a dictionary lookup cost with no library at all.

## The scenario

20 endpoint templates, 12 GET and 8 POST. For 200 endpoints the templates repeat with different ids, so every
path + query and every POST body is unique.

- **GET, path parameters:** `/users/{id}`, `/orders/{id}`, `/products/SKU-{n}`, `/customers/{id}/addresses`, `/invoices/{id}`
- **GET, query parameters:** `/users?page=&size=`, `/orders?status=&customer=&page=`, `/products?category=&q=&limit=`,
  `/inventory/SKU-{n}?warehouse=`, `/reports/daily?date=&tz=`, `/search?q=&limit=&offset=`, `/health`
- **POST, exact JSON body match:** `/users/{id}`, `/orders/{id}`, `/orders/{id}/cancel`, `/products/{id}`,
  `/payments/{id}`, `/invoices/{id}`, `/inventory/{id}/adjust`, `/customers/{id}`; some answer `201 Created`, some `200`
- Responses are JSON of 1-20 line items (roughly 150 bytes to 2 KB).

A stub matches on method, path, query string and (for POST) the exact request body. [`--check`](Program.cs) verifies
before any measuring that every fake returns the right resource for every endpoint and refuses an unknown path and a
POST with a different body.

## Results

Machine: Intel Core i7-6700K (4 cores / 8 threads), Windows 10, .NET SDK 10.0.300, runtime 10.0.12, workstation GC. BenchmarkDotNet 0.15.8, 3 warm-up and 12 measured iterations. Full tables:
[SetupBenchmarks](results/HttpClientMocking.SetupBenchmarks-report-github.md),
[RequestBenchmarks](results/HttpClientMocking.RequestBenchmarks-report-github.md),
[cold start](results/cold-start.txt).

### Setup (warm process)

`SetupFull` is everything a test pays: create the fake (including WireMock's server start), register the endpoints,
build the DI container, resolve the client, tear it all down. `RegisterOnly` is just the registrations on a fake that
is already running, which is what a shared fixture pays per test.

| Fake | SetupFull, 20 | SetupFull, 200 | RegisterOnly, 20 | RegisterOnly, 200 |
|---|---:|---:|---:|---:|
| Handwritten | 0.47 ms | 0.44 ms | 2 µs | 26 µs |
| HttpClientInterception | 0.49 ms | 1.45 ms | 59 µs | 325 µs |
| MockHttp | 0.47 ms | 0.74 ms | 15 µs | 177 µs |
| MoqContrib | **325 ms** | **3,943 ms** | 460 ms | 4,111 ms |
| WireMock | 4.9 ms | 11.8 ms | 0.37 ms | 4.0 ms |

About 0.45 ms of every `SetupFull` is the DI container and the `AddHttpClient` plumbing, which is why the cheap
rows look flat.

### Cold start (fresh process)

Median of 15 fresh processes: time from a freshly started process to 20 stubbed endpoints answering. This includes
loading the library and JIT, which is what the first test of a test run actually pays.

| Fake | create | start | register | provider | first call | other 19 | **total** |
|---|---:|---:|---:|---:|---:|---:|---:|
| Handwritten | 2 | 0 | 3 | 47 | 25 | 7 | **88 ms** |
| HttpClientInterception | 2 | 0 | 18 | 66 | 48 | 12 | **143 ms** |
| MockHttp | 5 | 0 | 12 | 64 | 49 | 20 | **160 ms** |
| MoqContrib | 18 | 0 | 376 | 109 | 34 | 10 | **568 ms** |
| WireMock | 0 | 274 | 13 | 26 | 949 | 130 | **1,495 ms** |

### Per request (warm)

One call through `ApiClient`, including JSON in and out. The single-call rows use an endpoint from the second half of
the registration list. "Average" is `AllEndpointsSequential` divided by the endpoint count.

| Fake | GET with query, 20 | GET with query, 200 | POST with body, 20 | POST with body, 200 | Average, 20 | Average, 200 |
|---|---:|---:|---:|---:|---:|---:|
| Handwritten | 14 µs | 17 µs | 8 µs | 7 µs | 12 µs | 10 µs |
| HttpClientInterception | 17 µs | 61 µs | 14 µs | 38 µs | 12 µs | 37 µs |
| MockHttp | 58 µs | 153 µs | 31 µs | 179 µs | 45 µs | 175 µs |
| MoqContrib | 32 µs | 39 µs | 33 µs | 35 µs | 25 µs | 36 µs |
| WireMock | 304 µs | 2,100 µs | 468 µs | 1,776 µs | 578 µs | 1,448 µs |

Allocation per request at 200 endpoints (GET with query): Handwritten 8.7 KB, MoqContrib 12.8 KB,
HttpClientInterception 37 KB, MockHttp 122 KB, WireMock 439 KB.

Sending all endpoints at once (`AllEndpointsParallel`) is about the same as sequential for the in-memory fakes. For
WireMock it is 5× faster at 20 endpoints (2.4 ms against 11.6 ms), since the server handles requests concurrently.

## Findings

1. **Nothing in-memory is slow per request at 20 endpoints.** The hand-written handler, HttpClientInterception and
   MockHttp all answer within 10-60 µs, and JSON serialization is a large part of that. A suite making thousands of
   stubbed calls will not notice the difference.
2. **MockHttp's per-request cost grows with the number of stubs.** It goes from about 45 µs average at 20 endpoints to
   175 µs at 200, and allocates 122 KB per request. Time and allocation both rise roughly in step with the list length, which
   points to stubs being tried one after another (I did not read its source to confirm). Fine for 20, worth knowing about for 200+.
3. **HttpClientInterception also grows, much less.** About 12 µs to 37 µs average from 20 to 200 endpoints.
4. **Moq.Contrib.HttpClient has a large registration cost.** Each `SetupRequest` costs about 16-20 ms warm (460 ms for
   20, 4.1 s for 200), and the cold-start register step is 376 ms. Once registered it is fast and flat per request
   (25 → 36 µs). That cost recurs wherever the setup does: per test with a fresh mock, it adds up quickly. I did not profile the
   cause; Moq builds and compiles expression trees for each setup, which would be consistent with it. Share one
   configured mock across tests if you use it.
5. **WireMock is a different kind of tool, and priced like one.** About 1.5 s to start and answer the first request
   in a fresh process, 0.3-0.5 ms per request at 20 endpoints and 1.8-2.1 ms at 200. Request time also scales roughly linearly with the number of mappings
   (about 10 µs per extra mapping, from the 20 → 200 jump). In exchange the HTTP is real: sockets, serialization, timeouts and TLS
   behave like production. Start one server per test class or assembly, never per test.
6. **Registering stubs once and sharing them is the fast pattern for all of them.** With the 0.45 ms DI floor, a
   per-test `SetupFull` is cheap for everything except Moq.Contrib. For WireMock, share the server and call
   `Reset()` (4 ms for 200 mappings) rather than restarting it.
7. **Matching strictness differs by default.** MockHttp and WireMock accept a request with an *extra* query parameter
   that no stub declared (they match the parameters they were told about). The other three refuse it. Both can be
   made strict (`WithExactQueryString` for MockHttp), which I did not use, so as to compare defaults.

## Caveats

- One machine, 12 iterations per benchmark. Several rows have standard deviations of 15-30% (the WireMock and
  MockHttp rows especially), so read these as orders of magnitude, not exact figures.
- The in-memory fakes are measured with `System.Text.Json` on both sides, so their numbers include a common cost of
  roughly 7-15 µs per call. The Handwritten row shows that floor.
- Defaults were used throughout, except that WireMock's request log is capped at 100 entries
  (`MaxRequestLogCount`), so memory does not grow across millions of requests, and `SetHandlerLifetime(Timeout.InfiniteTimeSpan)` is set
  so the factory never recycles the shared handler.
- Matching is on full URL + body. A library that is quicker when you match on method and path only is not rewarded
  here, and body matching is part of why MockHttp and WireMock allocate so much.
- Nothing was tuned per library beyond the above.

## Running it

```bash
cd HttpClientMocking
dotnet build -c Release
dotnet bin/Release/net10.0/HttpClientMocking.dll --check            # correctness only (~20 s)
dotnet bin/Release/net10.0/HttpClientMocking.dll --cold-all 15      # cold start, 15 fresh processes per fake
dotnet bin/Release/net10.0/HttpClientMocking.dll --filter '*'       # all BenchmarkDotNet runs (~15 min); e.g. --filter '*Setup*' for one class
```
