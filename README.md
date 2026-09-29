# Mikeys.Benchmarks

A collection of .NET micro-benchmarks, each with a written report of what was measured and what it shows.
Built with [BenchmarkDotNet](https://benchmarkdotnet.org/) on .NET 10.

| Benchmark | Question |
|---|---|
| [GuidSerialization](GuidSerialization/README.md) | What's the best way to put a `Guid` on the wire with protobuf-net / gRPC? Native vs big-endian vs string vs two longs vs a custom `Uuid` type |

## Running

```bash
cd <Benchmark>
dotnet run -c Release -- --filter '*'
```

Each benchmark project documents its own filters and options in its README.
