using System.Globalization;
using ProtoBuf;

namespace DecimalSerialization;

// protobuf-net's built-in decimal: bcl.Decimal { uint64 lo = 1; uint32 hi = 2; uint32 signScale = 3; }, a nested message.
[ProtoContract] public sealed class NativeMessage { [ProtoMember(1)] public List<decimal> Values { get; set; } = []; }

// Same wire bytes as NativeMessage, hand-written serializer.
[ProtoContract] public sealed class BclFastMessage { [ProtoMember(1)] public List<BclDecimalFast> Values { get; set; } = []; }

// Invariant-culture string, e.g. "123.45".
[ProtoContract] public sealed class StringMessage { [ProtoMember(1)] public List<string> Values { get; set; } = []; }

// protobuf-net 3 compatibility level 300's default for decimal: the same string, written without allocating one.
[ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
public sealed class L300Message { [ProtoMember(1)] public List<decimal> Values { get; set; } = []; }

// Same wire bytes as StringMessage, formatted/parsed straight to/from UTF-8.
[ProtoContract] public sealed class Utf8Message { [ProtoMember(1)] public List<Utf8Decimal> Values { get; set; } = []; }

[ProtoContract] public sealed class UnitsNanosMessage { [ProtoMember(1)] public List<UnitsNanos> Values { get; set; } = []; }

// One tag per value (protobuf-net doesn't pack custom scalar types).
[ProtoContract] public sealed class FixedDecimalMessage { [ProtoMember(1)] public List<FixedDecimal> Values { get; set; } = []; }
[ProtoContract] public sealed class Decimal64Message { [ProtoMember(1)] public List<Decimal64> Values { get; set; } = []; }
[ProtoContract] public sealed class PackedDecimal64Message { [ProtoMember(1)] public List<PackedDecimal64> Values { get; set; } = []; }
[ProtoContract] public sealed class Decimal128Message { [ProtoMember(1)] public List<Decimal128> Values { get; set; } = []; }

// Packed encoding (one tag + length for the whole list), via raw primitive arrays that protobuf-net packs natively.
// Byte-identical to proto3 `repeated sint64` / `repeated fixed64`.
[ProtoContract] public sealed class FixedDecimalPackedMessage { [ProtoMember(1, DataFormat = DataFormat.ZigZag, IsPacked = true)] public long[] Values { get; set; } = []; }
[ProtoContract] public sealed class Decimal64PackedMessage { [ProtoMember(1, DataFormat = DataFormat.FixedSize, IsPacked = true)] public ulong[] Values { get; set; } = []; }
[ProtoContract] public sealed class PackedDecimal64PackedMessage { [ProtoMember(1, DataFormat = DataFormat.ZigZag, IsPacked = true)] public long[] Values { get; set; } = []; }
// decimal128 as two packed fixed64 words per value: low, high, low, high...
[ProtoContract] public sealed class Decimal128PackedMessage { [ProtoMember(1, DataFormat = DataFormat.FixedSize, IsPacked = true)] public ulong[] Words { get; set; } = []; }

// The .NET side of proto/decimal_example.proto's Samples message - every format side by side, for --export.
[ProtoContract]
public sealed class Samples
{
    [ProtoMember(1)] public List<decimal> Native { get; set; } = [];
    [ProtoMember(2)] public List<string> Text { get; set; } = [];
    [ProtoMember(3)] public List<UnitsNanos> UnitsNanos { get; set; } = [];
    [ProtoMember(4, DataFormat = DataFormat.ZigZag, IsPacked = true)] public long[] FixedDecimal { get; set; } = [];
    [ProtoMember(5, DataFormat = DataFormat.FixedSize, IsPacked = true)] public ulong[] Decimal64 { get; set; } = [];
    [ProtoMember(6, DataFormat = DataFormat.ZigZag, IsPacked = true)] public long[] PackedDecimal64 { get; set; } = [];
    [ProtoMember(7)] public List<Decimal128> Decimal128 { get; set; } = [];
}

// decimal[] <-> message mapping, so every variant is measured end to end from the same domain type.
public static class Map
{
    public static List<T> ToList<T>(decimal[] values) where T : struct, IDecimalRepr<T>
    {
        var list = new List<T>(values.Length);
        foreach (var v in values) list.Add(T.FromDecimal(v));
        return list;
    }

    public static decimal[] ToArray<T>(List<T> list) where T : struct, IDecimalRepr<T>
    {
        var r = new decimal[list.Count];
        for (int i = 0; i < r.Length; i++) r[i] = list[i].ToDecimal();
        return r;
    }

    public static NativeMessage ToNative(decimal[] v) => new() { Values = [.. v] };
    public static decimal[] From(NativeMessage m) => [.. m.Values];

    public static BclFastMessage ToBclFast(decimal[] v) => new() { Values = ToList<BclDecimalFast>(v) };
    public static decimal[] From(BclFastMessage m) => ToArray(m.Values);

    public static L300Message ToL300(decimal[] v) => new() { Values = [.. v] };
    public static decimal[] From(L300Message m) => [.. m.Values];

    public static Utf8Message ToUtf8(decimal[] v) => new() { Values = ToList<Utf8Decimal>(v) };
    public static decimal[] From(Utf8Message m) => ToArray(m.Values);

    public static StringMessage ToStrings(decimal[] v)
    {
        var list = new List<string>(v.Length);
        foreach (var d in v) list.Add(d.ToString(CultureInfo.InvariantCulture));
        return new() { Values = list };
    }
    public static decimal[] From(StringMessage m)
    {
        var r = new decimal[m.Values.Count];
        for (int i = 0; i < r.Length; i++) r[i] = decimal.Parse(m.Values[i], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return r;
    }

    public static UnitsNanosMessage ToUnitsNanos(decimal[] v) => new() { Values = ToList<UnitsNanos>(v) };
    public static decimal[] From(UnitsNanosMessage m) => ToArray(m.Values);

    public static FixedDecimalMessage ToFixed(decimal[] v) => new() { Values = ToList<FixedDecimal>(v) };
    public static decimal[] From(FixedDecimalMessage m) => ToArray(m.Values);

    public static Decimal64Message ToDecimal64(decimal[] v) => new() { Values = ToList<Decimal64>(v) };
    public static decimal[] From(Decimal64Message m) => ToArray(m.Values);

    public static PackedDecimal64Message ToPacked(decimal[] v) => new() { Values = ToList<PackedDecimal64>(v) };
    public static decimal[] From(PackedDecimal64Message m) => ToArray(m.Values);

    public static Decimal128Message ToDecimal128(decimal[] v) => new() { Values = ToList<Decimal128>(v) };
    public static decimal[] From(Decimal128Message m) => ToArray(m.Values);

    public static long[] FixedRaw(decimal[] v)
    {
        var a = new long[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = FixedDecimal.FromDecimal(v[i]).Raw;
        return a;
    }
    public static FixedDecimalPackedMessage ToFixedPacked(decimal[] v) => new() { Values = FixedRaw(v) };
    public static decimal[] From(FixedDecimalPackedMessage m)
    {
        var r = new decimal[m.Values.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new FixedDecimal(m.Values[i]).ToDecimal();
        return r;
    }

    public static ulong[] Decimal64Raw(decimal[] v)
    {
        var a = new ulong[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = Decimal64.FromDecimal(v[i]).Bits;
        return a;
    }
    public static Decimal64PackedMessage ToDecimal64Packed(decimal[] v) => new() { Values = Decimal64Raw(v) };
    public static decimal[] From(Decimal64PackedMessage m)
    {
        var r = new decimal[m.Values.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new Decimal64(m.Values[i]).ToDecimal();
        return r;
    }

    public static long[] PackedRaw(decimal[] v)
    {
        var a = new long[v.Length];
        for (int i = 0; i < a.Length; i++) a[i] = PackedDecimal64.FromDecimal(v[i]).Raw;
        return a;
    }
    public static PackedDecimal64PackedMessage ToPackedPacked(decimal[] v) => new() { Values = PackedRaw(v) };
    public static decimal[] From(PackedDecimal64PackedMessage m)
    {
        var r = new decimal[m.Values.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new PackedDecimal64(m.Values[i]).ToDecimal();
        return r;
    }

    public static Decimal128PackedMessage ToDecimal128Packed(decimal[] v)
    {
        var words = new ulong[v.Length * 2];
        for (int i = 0; i < v.Length; i++)
        {
            var d = Decimal128.FromDecimal(v[i]);
            words[2 * i] = d.Low;
            words[2 * i + 1] = d.High;
        }
        return new() { Words = words };
    }
    public static decimal[] From(Decimal128PackedMessage m)
    {
        var r = new decimal[m.Words.Length / 2];
        for (int i = 0; i < r.Length; i++) r[i] = new Decimal128(m.Words[2 * i + 1], m.Words[2 * i]).ToDecimal();
        return r;
    }
}
