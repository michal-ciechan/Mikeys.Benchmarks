using System.Globalization;
using ProtoBuf;

namespace DecimalSerialization;

// protobuf-net's built-in decimal: bcl.Decimal { uint64 lo = 1; uint32 hi = 2; uint32 signScale = 3; }, a nested message.
[ProtoContract] public sealed class NativeMessage { [ProtoMember(1)] public List<decimal> Values { get; set; } = []; }

// Invariant-culture string, e.g. "123.45".
[ProtoContract] public sealed class StringMessage { [ProtoMember(1)] public List<string> Values { get; set; } = []; }

// protobuf-net 3 compatibility level 300's default for decimal.
[ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
public sealed class L300Message { [ProtoMember(1)] public List<decimal> Values { get; set; } = []; }

[ProtoContract] public sealed class UnitsNanosMessage { [ProtoMember(1)] public List<UnitsNanos> Values { get; set; } = []; }
[ProtoContract] public sealed class FixedDecimalMessage { [ProtoMember(1)] public List<FixedDecimal> Values { get; set; } = []; }
[ProtoContract] public sealed class Decimal64Message { [ProtoMember(1)] public List<Decimal64> Values { get; set; } = []; }
[ProtoContract] public sealed class PackedDecimal64Message { [ProtoMember(1)] public List<PackedDecimal64> Values { get; set; } = []; }

// The .NET side of proto/decimal_example.proto's Samples message - every format side by side, for --export.
[ProtoContract]
public sealed class Samples
{
    [ProtoMember(1)] public List<decimal> Native { get; set; } = [];
    [ProtoMember(2)] public List<string> Text { get; set; } = [];
    [ProtoMember(3)] public List<UnitsNanos> UnitsNanos { get; set; } = [];
    [ProtoMember(4)] public List<FixedDecimal> FixedDecimal { get; set; } = [];
    [ProtoMember(5)] public List<Decimal64> Decimal64 { get; set; } = [];
    [ProtoMember(6)] public List<PackedDecimal64> PackedDecimal64 { get; set; } = [];
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

    public static L300Message ToL300(decimal[] v) => new() { Values = [.. v] };
    public static decimal[] From(L300Message m) => [.. m.Values];

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
}
