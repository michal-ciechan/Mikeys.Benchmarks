using ProtoBuf;
using ProtoBuf.Serializers;

namespace DecimalSerialization;

/// <summary>A decimal representation that converts to and from System.Decimal.</summary>
public interface IDecimalRepr<TSelf> where TSelf : IDecimalRepr<TSelf>
{
    static abstract TSelf FromDecimal(decimal value);
    decimal ToDecimal();
}

/// <summary>System.Decimal is a 96-bit unsigned integer mantissa, a sign, and a scale 0-28 (value = ±mantissa / 10^scale).</summary>
internal static class DecimalParts
{
    public static readonly ulong[] Pow10 =
    [
        1UL, 10UL, 100UL, 1_000UL, 10_000UL, 100_000UL, 1_000_000UL, 10_000_000UL, 100_000_000UL, 1_000_000_000UL,
        10_000_000_000UL, 100_000_000_000UL, 1_000_000_000_000UL, 10_000_000_000_000UL, 100_000_000_000_000UL,
        1_000_000_000_000_000UL, 10_000_000_000_000_000UL, 100_000_000_000_000_000UL, 1_000_000_000_000_000_000UL,
        10_000_000_000_000_000_000UL,
    ];

    public static void Split(decimal d, out bool negative, out uint hi, out ulong lo, out int scale)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(d, bits);
        lo = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);
        hi = (uint)bits[2];
        negative = bits[3] < 0;
        scale = (bits[3] >> 16) & 0xFF;
    }

    public static UInt128 Mantissa(uint hi, ulong lo) => ((UInt128)hi << 64) | lo;

    public static UInt128 Pow10Wide(int n)
    {
        UInt128 r = 1;
        for (int i = 0; i < n; i++) r *= 10;
        return r;
    }

    public static decimal Make(bool negative, ulong mantissa, int scale) =>
        new((int)mantissa, (int)(mantissa >> 32), 0, negative, (byte)scale);
}

// ---------------------------------------------------------------------------------------------
// FixedDecimal: a fixed-point number, value × 10^8 held in a long. Wire: sint64 (zigzag varint).
// Range ±92,233,720,368.54775807, step 0.00000001. Anything finer than 8 dp is rejected, not rounded.
// Decoding always yields scale 8 (1.1 comes back as 1.10000000 - equal, but ToString differs).
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(FixedDecimalSerializer))]
public readonly struct FixedDecimal(long raw) : IDecimalRepr<FixedDecimal>
{
    public const int Scale = 8;
    public long Raw { get; } = raw;

    public static FixedDecimal FromDecimal(decimal value)
    {
        DecimalParts.Split(value, out var negative, out var hi, out var lo, out var scale);

        ulong magnitude;
        if (hi == 0 && scale <= Scale)
        {
            // Fast path: 64-bit mantissa, just scale up.
            ulong factor = DecimalParts.Pow10[Scale - scale];
            if (lo > long.MaxValue / factor)
                throw new OverflowException($"{value} is outside the FixedDecimal range (±92,233,720,368.54775807)");
            magnitude = lo * factor;
        }
        else
        {
            var m = DecimalParts.Mantissa(hi, lo);
            if (scale > Scale)
            {
                var divisor = DecimalParts.Pow10Wide(scale - Scale);
                if (m % divisor != 0)
                    throw new ArgumentException($"{value} has more than {Scale} decimal places");
                m /= divisor;
            }
            else
            {
                m *= DecimalParts.Pow10Wide(Scale - scale);
            }
            if (m > long.MaxValue)
                throw new OverflowException($"{value} is outside the FixedDecimal range (±92,233,720,368.54775807)");
            magnitude = (ulong)m;
        }

        return new FixedDecimal(negative ? -(long)magnitude : (long)magnitude);
    }

    public decimal ToDecimal()
    {
        bool negative = Raw < 0;
        ulong magnitude = negative ? ~(ulong)Raw + 1 : (ulong)Raw;
        return DecimalParts.Make(negative, magnitude, Scale);
    }
}

public sealed class FixedDecimalSerializer : ISerializer<FixedDecimal>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeSignedVarint;
    public FixedDecimal Read(ref ProtoReader.State state, FixedDecimal value) => new(state.ReadInt64());
    public void Write(ref ProtoWriter.State state, FixedDecimal value) => state.WriteInt64(value.Raw);
}

// ---------------------------------------------------------------------------------------------
// Decimal64: IEEE 754-2008 decimal64, binary integer decimal (BID) encoding. Wire: fixed64.
// 16 significant digits, exponent -398..369. Keeps the scale (1.10 stays 1.10).
// Converting from System.Decimal fails only if the value needs more than 16 significant digits;
// converting to it fails if the value is beyond ±7.9e28 or needs more than 28 decimal places.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(Decimal64Serializer))]
public readonly struct Decimal64(ulong bits) : IDecimalRepr<Decimal64>
{
    public const ulong MaxCoefficient = 9_999_999_999_999_999;
    const int Bias = 398;
    const ulong Low51 = (1UL << 51) - 1;
    const ulong Low53 = (1UL << 53) - 1;

    public ulong Bits { get; } = bits;

    public static Decimal64 FromDecimal(decimal value)
    {
        DecimalParts.Split(value, out var negative, out var hi, out var lo, out var scale);
        int exponent = -scale;

        ulong coefficient;
        if (hi == 0 && lo <= MaxCoefficient)
        {
            coefficient = lo;
        }
        else
        {
            // Too many digits: drop trailing zeros (raising the exponent) until it fits in 16 digits.
            var m = DecimalParts.Mantissa(hi, lo);
            while (m > MaxCoefficient && m % 10 == 0) { m /= 10; exponent++; }
            if (m > MaxCoefficient)
                throw new ArgumentException($"{value} needs more than 16 significant digits");
            coefficient = (ulong)m;
        }

        ulong sign = negative ? 1UL << 63 : 0;
        ulong biased = (ulong)(exponent + Bias);
        return new Decimal64(coefficient <= Low53
            ? sign | biased << 53 | coefficient
            : sign | 3UL << 61 | biased << 51 | (coefficient & Low51));
    }

    public decimal ToDecimal()
    {
        bool negative = (Bits >> 63) != 0;
        int biased;
        ulong coefficient;
        if (((Bits >> 61) & 3) == 3)
        {
            if (((Bits >> 59) & 3) == 3)
                throw new OverflowException("decimal64 infinity/NaN has no System.Decimal equivalent");
            biased = (int)((Bits >> 51) & 0x3FF);
            coefficient = (1UL << 53) | (Bits & Low51);
        }
        else
        {
            biased = (int)((Bits >> 53) & 0x3FF);
            coefficient = Bits & Low53;
        }
        if (coefficient > MaxCoefficient) coefficient = 0; // non-canonical encodings mean zero

        int exponent = biased - Bias;
        if (exponent > 0)
        {
            var r = DecimalParts.Make(negative, coefficient, 0);
            for (; exponent > 0; exponent--) r *= 10m; // throws OverflowException past ±7.9e28
            return r;
        }
        while (exponent < -28 && coefficient % 10 == 0) { coefficient /= 10; exponent++; }
        if (exponent < -28)
            throw new ArgumentException("decimal64 value has more than 28 decimal places");
        return DecimalParts.Make(negative, coefficient, -exponent);
    }
}

public sealed class Decimal64Serializer : ISerializer<Decimal64>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeFixed64;
    public Decimal64 Read(ref ProtoReader.State state, Decimal64 value) => new(state.ReadUInt64());
    public void Write(ref ProtoWriter.State state, Decimal64 value) => state.WriteUInt64(value.Bits);
}

// ---------------------------------------------------------------------------------------------
// PackedDecimal64: System.Decimal squeezed into 64 bits - a 59-bit signed mantissa and a 5-bit
// scale in the low bits (raw = mantissa << 5 | scale). Wire: sint64, so small values stay short.
// Mantissa ±288,230,376,151,711,743 (17 digits, some 18), scale 0-28 like System.Decimal.
// Keeps the scale unless trailing zeros had to be dropped to fit.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(PackedDecimal64Serializer))]
public readonly struct PackedDecimal64(long raw) : IDecimalRepr<PackedDecimal64>
{
    public const int ScaleBits = 5;
    public const long MaxMantissa = (1L << 58) - 1;

    public long Raw { get; } = raw;

    public static PackedDecimal64 FromDecimal(decimal value)
    {
        DecimalParts.Split(value, out var negative, out var hi, out var lo, out var scale);

        ulong mantissa;
        if (hi == 0 && lo <= MaxMantissa)
        {
            mantissa = lo;
        }
        else
        {
            var m = DecimalParts.Mantissa(hi, lo);
            while (m > MaxMantissa && scale > 0 && m % 10 == 0) { m /= 10; scale--; }
            if (m > MaxMantissa)
                throw scale == 0
                    ? new OverflowException($"{value} is beyond ±{MaxMantissa:N0}")
                    : new ArgumentException($"{value} needs more significant digits than a 59-bit mantissa holds");
            mantissa = (ulong)m;
        }

        long signed = negative ? -(long)mantissa : (long)mantissa;
        return new PackedDecimal64((signed << ScaleBits) | (long)scale);
    }

    public decimal ToDecimal()
    {
        int scale = (int)(Raw & 31);
        if (scale > 28) throw new ArgumentException($"PackedDecimal64 scale {scale} is above 28");
        long signed = Raw >> ScaleBits;
        bool negative = signed < 0;
        return DecimalParts.Make(negative, negative ? (ulong)-signed : (ulong)signed, scale);
    }
}

public sealed class PackedDecimal64Serializer : ISerializer<PackedDecimal64>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeSignedVarint;
    public PackedDecimal64 Read(ref ProtoReader.State state, PackedDecimal64 value) => new(state.ReadInt64());
    public void Write(ref ProtoWriter.State state, PackedDecimal64 value) => state.WriteInt64(value.Raw);
}

// ---------------------------------------------------------------------------------------------
// UnitsNanos: the google.type.Money layout - whole units (int64) + billionths (int32), same sign.
// Range ±9,223,372,036,854,775,807.999999999, step 0.000000001. A nested message on the wire.
// ---------------------------------------------------------------------------------------------
[ProtoContract]
public struct UnitsNanos : IDecimalRepr<UnitsNanos>
{
    [ProtoMember(1)] public long Units;
    [ProtoMember(2)] public int Nanos;

    public static UnitsNanos FromDecimal(decimal value)
    {
        if (decimal.Round(value, 9) != value)
            throw new ArgumentException($"{value} has more than 9 decimal places");
        var whole = decimal.Truncate(value);
        return new UnitsNanos
        {
            Units = decimal.ToInt64(whole), // throws OverflowException beyond ±9.2e18
            Nanos = (int)((value - whole) * 1_000_000_000m),
        };
    }

    public readonly decimal ToDecimal() => Units + Nanos / 1_000_000_000m;
}
