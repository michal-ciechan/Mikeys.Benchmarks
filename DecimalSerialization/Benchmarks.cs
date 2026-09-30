using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using ProtoBuf;

namespace DecimalSerialization;

public static class Data
{
    // Money/price-like values: up to 10 digits of mantissa, 0-8 decimal places, either sign.
    // Every format here can represent all of them exactly.
    public static decimal[] Make(int n)
    {
        var rng = new Random(42);
        var r = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            ulong mantissa = (ulong)rng.NextInt64(1, 10_000_000_000);
            int scale = rng.Next(0, 9);
            r[i] = new decimal((int)mantissa, (int)(mantissa >> 32), 0, rng.Next(2) == 0, (byte)scale);
        }
        return r;
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class DecimalBenchmarks
{
    [Params(1000)] public int N;

    decimal[] _values = [];
    decimal[] _back = [];
    readonly ArrayBufferWriter<byte> _buf = new(64 * 1024);
    readonly Dictionary<string, byte[]> _bytes = [];
    UnitsNanos[] _unTmp = [];
    FixedDecimal[] _fixedTmp = [];
    Decimal64[] _d64Tmp = [];
    PackedDecimal64[] _packedTmp = [];
    Decimal128[] _d128Tmp = [];

    [GlobalSetup]
    public void Setup()
    {
        DecimalParts.VerifyLayout();
        _values = Data.Make(N);
        _back = new decimal[N];
        _unTmp = new UnitsNanos[N];
        _fixedTmp = new FixedDecimal[N];
        _d64Tmp = new Decimal64[N];
        _packedTmp = new PackedDecimal64[N];
        _d128Tmp = new Decimal128[N];
        _bytes["native"] = Bytes(Map.ToNative(_values));
        _bytes["bclFast"] = Bytes(Map.ToBclFast(_values));
        _bytes["string"] = Bytes(Map.ToStrings(_values));
        _bytes["l300"] = Bytes(Map.ToL300(_values));
        _bytes["utf8"] = Bytes(Map.ToUtf8(_values));
        _bytes["unitsNanos"] = Bytes(Map.ToUnitsNanos(_values));
        _bytes["fixed"] = Bytes(Map.ToFixed(_values));
        _bytes["fixedPacked"] = Bytes(Map.ToFixedPacked(_values));
        _bytes["d64"] = Bytes(Map.ToDecimal64(_values));
        _bytes["d64Packed"] = Bytes(Map.ToDecimal64Packed(_values));
        _bytes["packed"] = Bytes(Map.ToPacked(_values));
        _bytes["packedPacked"] = Bytes(Map.ToPackedPacked(_values));
        _bytes["d128"] = Bytes(Map.ToDecimal128(_values));
        _bytes["d128Packed"] = Bytes(Map.ToDecimal128Packed(_values));
    }

    static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
    int Write<T>(T msg) { _buf.ResetWrittenCount(); Serializer.Serialize(_buf, msg); return _buf.WrittenCount; }
    T Read<T>(string key) => Serializer.Deserialize<T>((ReadOnlySpan<byte>)_bytes[key]);

    // --- conversion only: decimal -> representation -> decimal, no protobuf, no allocation ---
    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_UnitsNanos()
    {
        for (int i = 0; i < _values.Length; i++) _unTmp[i] = UnitsNanos.FromDecimal(_values[i]);
        for (int i = 0; i < _values.Length; i++) _back[i] = _unTmp[i].ToDecimal();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_FixedDecimal()
    {
        for (int i = 0; i < _values.Length; i++) _fixedTmp[i] = FixedDecimal.FromDecimal(_values[i]);
        for (int i = 0; i < _values.Length; i++) _back[i] = _fixedTmp[i].ToDecimal();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_Decimal64()
    {
        for (int i = 0; i < _values.Length; i++) _d64Tmp[i] = Decimal64.FromDecimal(_values[i]);
        for (int i = 0; i < _values.Length; i++) _back[i] = _d64Tmp[i].ToDecimal();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_PackedDecimal64()
    {
        for (int i = 0; i < _values.Length; i++) _packedTmp[i] = PackedDecimal64.FromDecimal(_values[i]);
        for (int i = 0; i < _values.Length; i++) _back[i] = _packedTmp[i].ToDecimal();
        return _back[^1];
    }

    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_Decimal128()
    {
        for (int i = 0; i < _values.Length; i++) _d128Tmp[i] = Decimal128.FromDecimal(_values[i]);
        for (int i = 0; i < _values.Length; i++) _back[i] = _d128Tmp[i].ToDecimal();
        return _back[^1];
    }

    // --- serialize (IBufferWriter), including the decimal[] -> message mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")] public int Ser_Native() => Write(Map.ToNative(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_BclFast() => Write(Map.ToBclFast(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_String() => Write(Map.ToStrings(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_L300() => Write(Map.ToL300(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Utf8() => Write(Map.ToUtf8(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_UnitsNanos() => Write(Map.ToUnitsNanos(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_FixedDecimal() => Write(Map.ToFixed(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_FixedDecimal_Packed() => Write(Map.ToFixedPacked(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Decimal64() => Write(Map.ToDecimal64(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Decimal64_Packed() => Write(Map.ToDecimal64Packed(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_PackedDecimal64() => Write(Map.ToPacked(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_PackedDecimal64_Packed() => Write(Map.ToPackedPacked(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Decimal128() => Write(Map.ToDecimal128(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Decimal128_Packed() => Write(Map.ToDecimal128Packed(_values));

    // --- deserialize, including the message -> decimal[] mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")] public decimal[] De_Native() => Map.From(Read<NativeMessage>("native"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_BclFast() => Map.From(Read<BclFastMessage>("bclFast"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_String() => Map.From(Read<StringMessage>("string"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_L300() => Map.From(Read<L300Message>("l300"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Utf8() => Map.From(Read<Utf8Message>("utf8"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_UnitsNanos() => Map.From(Read<UnitsNanosMessage>("unitsNanos"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_FixedDecimal() => Map.From(Read<FixedDecimalMessage>("fixed"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_FixedDecimal_Packed() => Map.From(Read<FixedDecimalPackedMessage>("fixedPacked"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Decimal64() => Map.From(Read<Decimal64Message>("d64"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Decimal64_Packed() => Map.From(Read<Decimal64PackedMessage>("d64Packed"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_PackedDecimal64() => Map.From(Read<PackedDecimal64Message>("packed"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_PackedDecimal64_Packed() => Map.From(Read<PackedDecimal64PackedMessage>("packedPacked"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Decimal128() => Map.From(Read<Decimal128Message>("d128"));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Decimal128_Packed() => Map.From(Read<Decimal128PackedMessage>("d128Packed"));
}
