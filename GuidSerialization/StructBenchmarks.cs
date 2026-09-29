using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using ProtoBuf;

namespace GuidSerialization;

// Why is MyGuidStruct slower than protobuf-net's built-in Guid? Separates:
//  - Convert:   endianness conversion alone (no protobuf)
//  - Serialize: IBufferWriter path (protobuf-net must measure each sub-message before writing it)
//  - SerializeStream: Stream path, for comparison
//  - Deserialize
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class StructBenchmarks
{
    [Params(1000)] public int N;

    Guid[] _ids = [];
    readonly ArrayBufferWriter<byte> _buf = new(64 * 1024);
    readonly MemoryStream _ms = new(64 * 1024);
    byte[] _native = [], _be = [], _crazy = [], _custom = [], _l300Default = [], _l300Fixed = [], _uuid = [];
    MyGuidStruct[] _beTmp = [];
    MyGuidStructCrazy[] _crazyTmp = [];
    Guid[] _back = [];

    [GlobalSetup]
    public void Setup()
    {
        _ids = Data.Make(N);
        _beTmp = new MyGuidStruct[N];
        _crazyTmp = new MyGuidStructCrazy[N];
        _back = new Guid[N];
        _native = Bytes(Map.ToNative(_ids));
        _be = Bytes(Map.ToStruct(_ids));
        _crazy = Bytes(Map.ToStructCrazy(_ids));
        _custom = Bytes(Map.ToStructCustom(_ids));
        _l300Default = Bytes(Map.ToL300Default(_ids));
        _l300Fixed = Bytes(Map.ToL300Fixed(_ids));
        _uuid = Bytes(Map.ToUuid(_ids));
    }

    static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
    int Write<T>(T msg) { _buf.ResetWrittenCount(); Serializer.Serialize(_buf, msg); return _buf.WrittenCount; }
    long WriteStream<T>(T msg) { _ms.Position = 0; _ms.SetLength(0); Serializer.Serialize(_ms, msg); return _ms.Length; }

    // --- conversion only: Guid -> two longs -> Guid, no allocation, no protobuf ---
    [Benchmark(Baseline = true), BenchmarkCategory("Convert")]
    public Guid Convert_CrazyEndian()
    {
        for (int i = 0; i < _ids.Length; i++) _crazyTmp[i] = MyGuidStructCrazy.From(_ids[i]);
        for (int i = 0; i < _ids.Length; i++) _back[i] = _crazyTmp[i].ToGuid();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public Guid Convert_BigEndian()
    {
        for (int i = 0; i < _ids.Length; i++) _beTmp[i] = MyGuidStruct.From(_ids[i]);
        for (int i = 0; i < _ids.Length; i++) _back[i] = _beTmp[i].ToGuid();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public Guid Convert_BigEndianFast()
    {
        for (int i = 0; i < _ids.Length; i++) _beTmp[i] = MyGuidStruct.FromFast(_ids[i]);
        for (int i = 0; i < _ids.Length; i++) _back[i] = _beTmp[i].ToGuidFast();
        return _back[^1];
    }

    // --- serialize via IBufferWriter ---
    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")] public int Ser_Native() => Write(Map.ToNative(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_StructBigEndian() => Write(Map.ToStruct(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_StructCrazyEndian() => Write(Map.ToStructCrazy(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_StructCustomSerializer() => Write(Map.ToStructCustom(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Uuid() => Write(Map.ToUuid(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_L300Default() => Write(Map.ToL300Default(_ids));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_L300FixedSize() => Write(Map.ToL300Fixed(_ids));

    // --- serialize via Stream ---
    [Benchmark(Baseline = true), BenchmarkCategory("SerializeStream")] public long Stream_Native() => WriteStream(Map.ToNative(_ids));
    [Benchmark, BenchmarkCategory("SerializeStream")] public long Stream_StructBigEndian() => WriteStream(Map.ToStruct(_ids));
    [Benchmark, BenchmarkCategory("SerializeStream")] public long Stream_StructCrazyEndian() => WriteStream(Map.ToStructCrazy(_ids));
    [Benchmark, BenchmarkCategory("SerializeStream")] public long Stream_StructCustomSerializer() => WriteStream(Map.ToStructCustom(_ids));
    [Benchmark, BenchmarkCategory("SerializeStream")] public long Stream_Uuid() => WriteStream(Map.ToUuid(_ids));

    // --- deserialize ---
    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")] public Guid[] De_Native() => Map.From(Serializer.Deserialize<NativeGuidMessage>((ReadOnlySpan<byte>)_native));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_StructBigEndian() => Map.From(Serializer.Deserialize<StructMessage>((ReadOnlySpan<byte>)_be));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_StructCrazyEndian() => Map.From(Serializer.Deserialize<StructCrazyMessage>((ReadOnlySpan<byte>)_crazy));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_StructCustomSerializer() => Map.From(Serializer.Deserialize<StructCustomMessage>((ReadOnlySpan<byte>)_custom));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_Uuid() => Map.From(Serializer.Deserialize<UuidMessage>((ReadOnlySpan<byte>)_uuid));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_L300Default() => Map.From(Serializer.Deserialize<L300DefaultMessage>((ReadOnlySpan<byte>)_l300Default));
    [Benchmark, BenchmarkCategory("Deserialize")] public Guid[] De_L300FixedSize() => Map.From(Serializer.Deserialize<L300FixedMessage>((ReadOnlySpan<byte>)_l300Fixed));
}
