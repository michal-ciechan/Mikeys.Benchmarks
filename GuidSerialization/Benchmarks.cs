using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using ProtoBuf;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Server;

namespace GuidSerialization;

public static class Data
{
    public static Guid[] Make(int n)
    {
        var rng = new Random(42);
        var r = new Guid[n];
        Span<byte> b = stackalloc byte[16];
        for (int i = 0; i < n; i++) { rng.NextBytes(b); r[i] = new Guid(b); }
        return r;
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class SerializationBenchmarks
{
    [Params(1, 1000)] public int N;

    Guid[] _ids = [];
    readonly ArrayBufferWriter<byte> _buf = new(64 * 1024);
    byte[] _native = [], _nativeBytes = [], _bigEndian = [], _string = [], _struct = [], _class = [];

    [GlobalSetup]
    public void Setup()
    {
        _ids = Data.Make(N);
        _native = Bytes(Map.ToNative(_ids));
        _nativeBytes = Bytes(Map.ToNativeBytes(_ids));
        _bigEndian = Bytes(Map.ToBigEndian(_ids));
        _string = Bytes(Map.ToStrings(_ids));
        _struct = Bytes(Map.ToStruct(_ids));
        _class = Bytes(Map.ToClass(_ids));
    }

    static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }

    int Write<T>(T msg) { _buf.ResetWrittenCount(); Serializer.Serialize(_buf, msg); return _buf.WrittenCount; }

    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")] public int Ser_Native() => Write(Map.ToNative(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_NativeBytes() => Write(Map.ToNativeBytes(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_BigEndianBytes() => Write(Map.ToBigEndian(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_String() => Write(Map.ToStrings(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_MyGuidStruct() => Write(Map.ToStruct(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_MyGuidClass() => Write(Map.ToClass(_ids));

    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")] public Guid[] De_Native() => Map.From(Serializer.Deserialize<NativeGuidMessage>((ReadOnlySpan<byte>)_native));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_NativeBytes() => Map.From(Serializer.Deserialize<NativeBytesMessage>((ReadOnlySpan<byte>)_nativeBytes));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_BigEndianBytes() => Map.From(Serializer.Deserialize<BigEndianBytesMessage>((ReadOnlySpan<byte>)_bigEndian));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_String() => Map.From(Serializer.Deserialize<StringMessage>((ReadOnlySpan<byte>)_string));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_MyGuidStruct() => Map.From(Serializer.Deserialize<StructMessage>((ReadOnlySpan<byte>)_struct));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_MyGuidClass() => Map.From(Serializer.Deserialize<ClassMessage>((ReadOnlySpan<byte>)_class));
}

// Full unary round trip: client maps Guid[] -> DTO, serialises, HTTP/2 to an in-process Kestrel
// server that deserialises + echoes it back, client deserialises and maps back to Guid[].
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class GrpcBenchmarks
{
    const int Port = 50151;

    [Params(1, 1000)] public int N;

    Guid[] _ids = [];
    WebApplication? _app;
    GrpcChannel? _channel;
    IEchoService _client = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _ids = Data.Make(N);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(Port, o => o.Protocols = HttpProtocols.Http2));
        builder.Services.AddCodeFirstGrpc(o => o.MaxReceiveMessageSize = 16 * 1024 * 1024);
        _app = builder.Build();
        _app.MapGrpcService<EchoService>();
        await _app.StartAsync();

        _channel = GrpcChannel.ForAddress($"http://localhost:{Port}");
        _client = _channel.CreateGrpcService<IEchoService>();
        await _client.Native(Map.ToNative(_ids)); // open the HTTP/2 connection before measuring
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _channel?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    [Benchmark(Baseline = true)] public async Task<Guid[]> Grpc_Native() => Map.From(await _client.Native(Map.ToNative(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_NativeBytes() => Map.From(await _client.NativeBytes(Map.ToNativeBytes(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_BigEndianBytes() => Map.From(await _client.BigEndian(Map.ToBigEndian(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_String() => Map.From(await _client.String(Map.ToStrings(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_MyGuidStruct() => Map.From(await _client.Struct(Map.ToStruct(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_MyGuidClass() => Map.From(await _client.Class(Map.ToClass(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_StructCustomSerializer() => Map.From(await _client.StructCustom(Map.ToStructCustom(_ids)));
    [Benchmark] public async Task<Guid[]> Grpc_Uuid() => Map.From(await _client.Uuid(Map.ToUuid(_ids)));
}
