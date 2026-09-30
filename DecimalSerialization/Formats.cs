using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace DecimalSerialization;

/// <summary>A decimal representation that converts to and from System.Decimal.</summary>
public interface IDecimalRepr<TSelf> where TSelf : IDecimalRepr<TSelf>
{
    static abstract TSelf FromDecimal(decimal value);
    decimal ToDecimal();
}

/// <summary>
/// System.Decimal's in-memory layout on .NET Core 3.0+: flags (sign bit 31, scale in bits 16-23), then the 96-bit
/// mantissa as a uint (high 32 bits) and a ulong (low 64 bits). Reading it directly avoids decimal.GetBits.
/// This is an implementation detail, so <see cref="DecimalParts.VerifyLayout"/> checks it at startup.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DecimalLayout
{
    public int Flags;
    public uint Hi32;
    public ulong Lo64;

    public readonly bool Negative => Flags < 0;
    public readonly int Scale => (Flags >> 16) & 0xFF;
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

    public static readonly UInt128 MaxMantissa = (UInt128.One << 96) - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DecimalLayout Of(decimal d) => Unsafe.As<decimal, DecimalLayout>(ref d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Make(bool negative, uint hi, ulong lo, int scale)
    {
        var layout = new DecimalLayout { Flags = (scale << 16) | (negative ? int.MinValue : 0), Hi32 = hi, Lo64 = lo };
        return Unsafe.As<DecimalLayout, decimal>(ref layout);
    }

    public static decimal Make(bool negative, UInt128 mantissa, int scale) =>
        Make(negative, (uint)(mantissa >> 64), (ulong)mantissa, scale);

    public static UInt128 Mantissa(in DecimalLayout p) => ((UInt128)p.Hi32 << 64) | p.Lo64;

    public static UInt128 Pow10Wide(int n)
    {
        UInt128 r = 1;
        for (int i = 0; i < n; i++) r *= 10;
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int VarintLength(ulong v) => (64 - BitOperations.LeadingZeroCount(v | 1) + 6) / 7;

    /// <summary>Fails loudly if System.Decimal's layout ever stops matching <see cref="DecimalLayout"/>.</summary>
    public static void VerifyLayout()
    {
        Span<int> bits = stackalloc int[4];
        foreach (var d in new[] { 0m, 1m, -1.5m, 123.45m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m })
        {
            decimal.GetBits(d, bits);
            var p = Of(d);
            if (p.Flags != bits[3] || p.Hi32 != (uint)bits[2] || p.Lo64 != ((uint)bits[0] | ((ulong)(uint)bits[1] << 32))
                || Make(p.Negative, p.Hi32, p.Lo64, p.Scale) != d)
                throw new InvalidOperationException("System.Decimal's memory layout is not the expected flags/hi32/lo64");
        }
    }
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

    // Largest 64-bit mantissa at each scale 0..8 that still fits in a long once scaled to 8 dp.
    static readonly ulong[] MaxBeforeScaling = [.. Enumerable.Range(0, Scale + 1).Select(s => (ulong)long.MaxValue / DecimalParts.Pow10[Scale - s])];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixedDecimal FromDecimal(decimal value)
    {
        var p = DecimalParts.Of(value);
        int scale = p.Scale;
        if (p.Hi32 == 0 && scale <= Scale && p.Lo64 <= MaxBeforeScaling[scale])
        {
            long magnitude = (long)(p.Lo64 * DecimalParts.Pow10[Scale - scale]);
            return new FixedDecimal(p.Negative ? -magnitude : magnitude);
        }
        return FromDecimalSlow(value, p);
    }

    static FixedDecimal FromDecimalSlow(decimal value, DecimalLayout p)
    {
        var m = DecimalParts.Mantissa(p);
        int scale = p.Scale;
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
        return new FixedDecimal(p.Negative ? -(long)m : (long)m);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ToDecimal()
    {
        bool negative = Raw < 0;
        return DecimalParts.Make(negative, 0, negative ? ~(ulong)Raw + 1 : (ulong)Raw, Scale);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal64 FromDecimal(decimal value)
    {
        var p = DecimalParts.Of(value);
        if (p.Hi32 != 0 || p.Lo64 > MaxCoefficient)
            return FromDecimalSlow(value, p);
        return Encode(p.Negative, p.Lo64, -p.Scale);
    }

    static Decimal64 FromDecimalSlow(decimal value, DecimalLayout p)
    {
        // Too many digits: drop trailing zeros (raising the exponent) until it fits in 16 digits.
        var m = DecimalParts.Mantissa(p);
        int exponent = -p.Scale;
        while (m > MaxCoefficient && m % 10 == 0) { m /= 10; exponent++; }
        if (m > MaxCoefficient)
            throw new ArgumentException($"{value} needs more than 16 significant digits");
        return Encode(p.Negative, (ulong)m, exponent);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Decimal64 Encode(bool negative, ulong coefficient, int exponent)
    {
        ulong sign = negative ? 1UL << 63 : 0;
        ulong biased = (ulong)(exponent + Bias);
        return new Decimal64(coefficient <= Low53
            ? sign | biased << 53 | coefficient
            : sign | 3UL << 61 | biased << 51 | (coefficient & Low51));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            if (coefficient > MaxCoefficient) coefficient = 0; // non-canonical encodings mean zero
        }
        else
        {
            biased = (int)((Bits >> 53) & 0x3FF);
            coefficient = Bits & Low53;
        }

        int exponent = biased - Bias;
        if (exponent is <= 0 and >= -28)
            return DecimalParts.Make(negative, 0, coefficient, -exponent);
        return ToDecimalSlow(negative, coefficient, exponent);
    }

    static decimal ToDecimalSlow(bool negative, ulong coefficient, int exponent)
    {
        if (exponent > 0)
        {
            var r = DecimalParts.Make(negative, 0, coefficient, 0);
            for (; exponent > 0; exponent--) r *= 10m; // throws OverflowException past ±7.9e28
            return r;
        }
        while (exponent < -28 && coefficient % 10 == 0) { coefficient /= 10; exponent++; }
        if (exponent < -28)
            throw new ArgumentException("decimal64 value has more than 28 decimal places");
        return DecimalParts.Make(negative, 0, coefficient, -exponent);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PackedDecimal64 FromDecimal(decimal value)
    {
        var p = DecimalParts.Of(value);
        if (p.Hi32 != 0 || p.Lo64 > MaxMantissa)
            return FromDecimalSlow(value, p);
        long signed = p.Negative ? -(long)p.Lo64 : (long)p.Lo64;
        return new PackedDecimal64((signed << ScaleBits) | (long)p.Scale);
    }

    static PackedDecimal64 FromDecimalSlow(decimal value, DecimalLayout p)
    {
        var m = DecimalParts.Mantissa(p);
        int scale = p.Scale;
        while (m > MaxMantissa && scale > 0 && m % 10 == 0) { m /= 10; scale--; }
        if (m > MaxMantissa)
            throw scale == 0
                ? new OverflowException($"{value} is beyond ±{MaxMantissa:N0}")
                : new ArgumentException($"{value} needs more significant digits than a 59-bit mantissa holds");
        long signed = p.Negative ? -(long)m : (long)m;
        return new PackedDecimal64((signed << ScaleBits) | (long)scale);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ToDecimal()
    {
        int scale = (int)(Raw & 31);
        if (scale > 28) throw new ArgumentException($"PackedDecimal64 scale {scale} is above 28");
        long signed = Raw >> ScaleBits;
        bool negative = signed < 0;
        return DecimalParts.Make(negative, 0, negative ? (ulong)-signed : (ulong)signed, scale);
    }
}

public sealed class PackedDecimal64Serializer : ISerializer<PackedDecimal64>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeSignedVarint;
    public PackedDecimal64 Read(ref ProtoReader.State state, PackedDecimal64 value) => new(state.ReadInt64());
    public void Write(ref ProtoWriter.State state, PackedDecimal64 value) => state.WriteInt64(value.Raw);
}

// ---------------------------------------------------------------------------------------------
// Decimal128: IEEE 754-2008 decimal128, BID encoding - 34 significant digits, so it holds every
// System.Decimal exactly (96-bit mantissa = at most 29 digits). Wire: 16-byte `bytes`, low 64 bits first.
// Keeps the scale.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(Decimal128Serializer))]
public readonly struct Decimal128(ulong high, ulong low) : IDecimalRepr<Decimal128>
{
    const int Bias = 6176;
    const ulong Low49 = (1UL << 49) - 1;
    static readonly UInt128 MaxCoefficient = UInt128.Parse("9999999999999999999999999999999999"); // 10^34 - 1

    public ulong High { get; } = high;
    public ulong Low { get; } = low;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128 FromDecimal(decimal value)
    {
        // The 96-bit mantissa always fits the 113-bit coefficient field, so this never fails.
        var p = DecimalParts.Of(value);
        ulong sign = p.Negative ? 1UL << 63 : 0;
        return new Decimal128(sign | (ulong)(Bias - p.Scale) << 49 | p.Hi32, p.Lo64);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ToDecimal()
    {
        bool negative = (High >> 63) != 0;
        if (((High >> 61) & 3) == 3)
        {
            if (((High >> 59) & 3) == 3)
                throw new OverflowException("decimal128 infinity/NaN has no System.Decimal equivalent");
            return DecimalParts.Make(negative, 0, 0, 0); // large-coefficient form is always non-canonical: zero
        }

        int exponent = (int)((High >> 49) & 0x3FFF) - Bias;
        ulong coefficientHigh = High & Low49;
        if (coefficientHigh <= uint.MaxValue && exponent is <= 0 and >= -28)
            return DecimalParts.Make(negative, (uint)coefficientHigh, Low, -exponent);
        return ToDecimalSlow(negative, ((UInt128)coefficientHigh << 64) | Low, exponent);
    }

    static decimal ToDecimalSlow(bool negative, UInt128 coefficient, int exponent)
    {
        if (coefficient > MaxCoefficient) return DecimalParts.Make(negative, 0, 0, 0); // non-canonical: zero

        // Wider than 96 bits, or more than 28 decimal places: drop trailing zeros where possible.
        while ((coefficient > DecimalParts.MaxMantissa || exponent < -28) && exponent < 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            exponent++;
        }
        while (exponent > 0 && coefficient <= DecimalParts.MaxMantissa)
        {
            coefficient *= 10;
            exponent--;
        }
        if (coefficient > DecimalParts.MaxMantissa || exponent > 0)
            throw exponent >= 0
                ? new OverflowException("decimal128 value is beyond ±7.9e28")
                : new ArgumentException("decimal128 value needs more than 29 significant digits");
        if (exponent < -28)
            throw new ArgumentException("decimal128 value has more than 28 decimal places");
        return DecimalParts.Make(negative, coefficient, -exponent);
    }
}

public sealed class Decimal128Serializer : ISerializer<Decimal128>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

    // Pointer-backed spans: the reader/writer are ref structs, so the compiler rejects a stackalloc span
    // argument (CS8350). Both calls copy before returning, so the buffer never outlives the frame.
    [SkipLocalsInit]
    public unsafe Decimal128 Read(ref ProtoReader.State state, Decimal128 value)
    {
        byte* p = stackalloc byte[16];
        var read = state.ReadBytes(new Span<byte>(p, 16));
        if (read.Length != 16) throw new ProtoException($"decimal128 field must be 16 bytes, got {read.Length}");
        return new Decimal128(BinaryPrimitives.ReadUInt64LittleEndian(read[8..]), BinaryPrimitives.ReadUInt64LittleEndian(read));
    }

    [SkipLocalsInit]
    public unsafe void Write(ref ProtoWriter.State state, Decimal128 value)
    {
        byte* p = stackalloc byte[16];
        var buffer = new Span<byte>(p, 16);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..], value.High);
        state.WriteBytes((ReadOnlySpan<byte>)buffer);
    }
}

// ---------------------------------------------------------------------------------------------
// BclDecimalFast: byte-for-byte the same wire format as protobuf-net's built-in decimal
// (bcl.Decimal { uint64 lo = 1; uint32 hi = 2; uint32 signScale = 3; }), with a hand-written
// serializer that reports its own length and reads the decimal's bits directly.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(BclDecimalFastSerializer))]
public readonly struct BclDecimalFast(decimal value) : IDecimalRepr<BclDecimalFast>
{
    public decimal Value { get; } = value;
    public static BclDecimalFast FromDecimal(decimal value) => new(value);
    public decimal ToDecimal() => Value;
}

public sealed class BclDecimalFastSerializer : IMeasuringSerializer<BclDecimalFast>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryMessage | SerializerFeatures.WireTypeString;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint SignScale(in DecimalLayout p) => (uint)(p.Scale << 1) | (p.Negative ? 1u : 0u);

    public int Measure(ISerializationContext context, WireType wireType, BclDecimalFast value)
    {
        var p = DecimalParts.Of(value.Value);
        uint signScale = SignScale(p);
        return (p.Lo64 != 0 ? 1 + DecimalParts.VarintLength(p.Lo64) : 0)
             + (p.Hi32 != 0 ? 1 + DecimalParts.VarintLength(p.Hi32) : 0)
             + (signScale != 0 ? 1 + DecimalParts.VarintLength(signScale) : 0);
    }

    public void Write(ref ProtoWriter.State state, BclDecimalFast value)
    {
        var p = DecimalParts.Of(value.Value);
        uint signScale = SignScale(p);
        if (p.Lo64 != 0) { state.WriteFieldHeader(1, WireType.Varint); state.WriteUInt64(p.Lo64); }
        if (p.Hi32 != 0) { state.WriteFieldHeader(2, WireType.Varint); state.WriteUInt32(p.Hi32); }
        if (signScale != 0) { state.WriteFieldHeader(3, WireType.Varint); state.WriteUInt32(signScale); }
    }

    public BclDecimalFast Read(ref ProtoReader.State state, BclDecimalFast value)
    {
        ulong lo = 0;
        uint hi = 0, signScale = 0;
        int field;
        while ((field = state.ReadFieldHeader()) > 0)
        {
            switch (field)
            {
                case 1: lo = state.ReadUInt64(); break;
                case 2: hi = state.ReadUInt32(); break;
                case 3: signScale = state.ReadUInt32(); break;
                default: state.SkipField(); break;
            }
        }
        int scale = (int)(signScale >> 1);
        if (scale > 28) throw new ProtoException($"bcl.Decimal scale {scale} is above 28");
        return new BclDecimalFast(DecimalParts.Make((signScale & 1) != 0, hi, lo, scale));
    }
}

// ---------------------------------------------------------------------------------------------
// Utf8Decimal: the same wire format as a plain string field ("123.45"), but formatted and parsed
// straight to/from UTF-8 in a stack buffer - no string allocated.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(Utf8DecimalSerializer))]
public readonly struct Utf8Decimal(decimal value) : IDecimalRepr<Utf8Decimal>
{
    public decimal Value { get; } = value;
    public static Utf8Decimal FromDecimal(decimal value) => new(value);
    public decimal ToDecimal() => Value;
}

public sealed class Utf8DecimalSerializer : ISerializer<Utf8Decimal>
{
    const int MaxLength = 64; // decimal's longest invariant form is 31 characters
    const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

    [SkipLocalsInit]
    public unsafe Utf8Decimal Read(ref ProtoReader.State state, Utf8Decimal value)
    {
        byte* p = stackalloc byte[MaxLength];
        var read = state.ReadBytes(new Span<byte>(p, MaxLength));
        return new Utf8Decimal(decimal.Parse(read, Styles, CultureInfo.InvariantCulture));
    }

    [SkipLocalsInit]
    public unsafe void Write(ref ProtoWriter.State state, Utf8Decimal value)
    {
        byte* p = stackalloc byte[MaxLength];
        var buffer = new Span<byte>(p, MaxLength);
        value.Value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
        state.WriteBytes((ReadOnlySpan<byte>)buffer[..written]);
    }
}

// ---------------------------------------------------------------------------------------------
// UnitsNanos: the google.type.Money layout - whole units (int64) + billionths (int32), same sign.
// Range ±9,223,372,036,854,775,807.999999999, step 0.000000001. A nested message on the wire.
// Hand-written serializer (reports its own length) and integer-only conversion. Decodes to scale 9.
// ---------------------------------------------------------------------------------------------
[ProtoContract(Serializer = typeof(UnitsNanosSerializer))]
public readonly struct UnitsNanos(long units, int nanos) : IDecimalRepr<UnitsNanos>
{
    const int NanoScale = 9;

    public long Units { get; } = units;
    public int Nanos { get; } = nanos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UnitsNanos FromDecimal(decimal value)
    {
        var p = DecimalParts.Of(value);
        int scale = p.Scale;
        if (p.Hi32 == 0 && scale <= NanoScale)
        {
            ulong divisor = DecimalParts.Pow10[scale];
            ulong units = p.Lo64 / divisor;
            if (units <= long.MaxValue)
            {
                long nanos = (long)((p.Lo64 - units * divisor) * DecimalParts.Pow10[NanoScale - scale]);
                return p.Negative ? new UnitsNanos(-(long)units, (int)-nanos) : new UnitsNanos((long)units, (int)nanos);
            }
        }
        return FromDecimalSlow(value);
    }

    static UnitsNanos FromDecimalSlow(decimal value)
    {
        if (decimal.Round(value, NanoScale) != value)
            throw new ArgumentException($"{value} has more than 9 decimal places");
        var whole = decimal.Truncate(value);
        return new UnitsNanos(decimal.ToInt64(whole) /* throws OverflowException beyond ±9.2e18 */,
                              (int)((value - whole) * 1_000_000_000m));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ToDecimal()
    {
        Int128 total = (Int128)Units * 1_000_000_000 + Nanos;
        bool negative = total < 0;
        return DecimalParts.Make(negative, (UInt128)(negative ? -total : total), NanoScale);
    }
}

public sealed class UnitsNanosSerializer : IMeasuringSerializer<UnitsNanos>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryMessage | SerializerFeatures.WireTypeString;

    // Plain int64/int32 varints: negative values sign-extend to 10 bytes.
    public int Measure(ISerializationContext context, WireType wireType, UnitsNanos value) =>
        (value.Units != 0 ? 1 + DecimalParts.VarintLength((ulong)value.Units) : 0)
      + (value.Nanos != 0 ? 1 + DecimalParts.VarintLength((ulong)(long)value.Nanos) : 0);

    public void Write(ref ProtoWriter.State state, UnitsNanos value)
    {
        if (value.Units != 0) { state.WriteFieldHeader(1, WireType.Varint); state.WriteInt64(value.Units); }
        if (value.Nanos != 0) { state.WriteFieldHeader(2, WireType.Varint); state.WriteInt32(value.Nanos); }
    }

    public UnitsNanos Read(ref ProtoReader.State state, UnitsNanos value)
    {
        long units = 0;
        int nanos = 0, field;
        while ((field = state.ReadFieldHeader()) > 0)
        {
            switch (field)
            {
                case 1: units = state.ReadInt64(); break;
                case 2: nanos = state.ReadInt32(); break;
                default: state.SkipField(); break;
            }
        }
        return new UnitsNanos(units, nanos);
    }
}
