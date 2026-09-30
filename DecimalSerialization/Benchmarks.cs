using System.Buffers;
using System.Globalization;
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
    byte[] _native = [], _string = [], _l300 = [], _unitsNanos = [], _fixed = [], _d64 = [], _packed = [];
    UnitsNanos[] _unTmp = [];
    FixedDecimal[] _fixedTmp = [];
    Decimal64[] _d64Tmp = [];
    PackedDecimal64[] _packedTmp = [];
    string[] _strTmp = [];

    [GlobalSetup]
    public void Setup()
    {
        _values = Data.Make(N);
        _back = new decimal[N];
        _unTmp = new UnitsNanos[N];
        _fixedTmp = new FixedDecimal[N];
        _d64Tmp = new Decimal64[N];
        _packedTmp = new PackedDecimal64[N];
        _strTmp = new string[N];
        _native = Bytes(Map.ToNative(_values));
        _string = Bytes(Map.ToStrings(_values));
        _l300 = Bytes(Map.ToL300(_values));
        _unitsNanos = Bytes(Map.ToUnitsNanos(_values));
        _fixed = Bytes(Map.ToFixed(_values));
        _d64 = Bytes(Map.ToDecimal64(_values));
        _packed = Bytes(Map.ToPacked(_values));
    }

    static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
    int Write<T>(T msg) { _buf.ResetWrittenCount(); Serializer.Serialize(_buf, msg); return _buf.WrittenCount; }

    // --- conversion only: decimal -> representation -> decimal, no protobuf, no allocation (except String) ---
    [Benchmark, BenchmarkCategory("Convert")]
    public decimal Convert_String()
    {
        for (int i = 0; i < _values.Length; i++) _strTmp[i] = _values[i].ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < _values.Length; i++) _back[i] = decimal.Parse(_strTmp[i], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return _back[^1];
    }

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

    // --- serialize (IBufferWriter), including the decimal[] -> message mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")] public int Ser_Native() => Write(Map.ToNative(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_String() => Write(Map.ToStrings(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_L300() => Write(Map.ToL300(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_UnitsNanos() => Write(Map.ToUnitsNanos(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_FixedDecimal() => Write(Map.ToFixed(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_Decimal64() => Write(Map.ToDecimal64(_values));
    [Benchmark, BenchmarkCategory("Serialize")] public int Ser_PackedDecimal64() => Write(Map.ToPacked(_values));

    // --- deserialize, including the message -> decimal[] mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")] public decimal[] De_Native() => Map.From(Serializer.Deserialize<NativeMessage>((ReadOnlySpan<byte>)_native));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_String() => Map.From(Serializer.Deserialize<StringMessage>((ReadOnlySpan<byte>)_string));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_L300() => Map.From(Serializer.Deserialize<L300Message>((ReadOnlySpan<byte>)_l300));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_UnitsNanos() => Map.From(Serializer.Deserialize<UnitsNanosMessage>((ReadOnlySpan<byte>)_unitsNanos));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_FixedDecimal() => Map.From(Serializer.Deserialize<FixedDecimalMessage>((ReadOnlySpan<byte>)_fixed));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_Decimal64() => Map.From(Serializer.Deserialize<Decimal64Message>((ReadOnlySpan<byte>)_d64));
    [Benchmark, BenchmarkCategory("Deserialize")] public decimal[] De_PackedDecimal64() => Map.From(Serializer.Deserialize<PackedDecimal64Message>((ReadOnlySpan<byte>)_packed));
}
