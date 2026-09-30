# Decimal serialization with protobuf-net: decimal, fixed-point, decimal64 and decimal128

How should a `System.Decimal` go over the wire with [protobuf-net](https://github.com/protobuf-net/protobuf-net)?
This project compares protobuf-net's built-in format, strings, Google's units+nanos layout, a fixed-point
integer (`FixedDecimal`), IEEE 754 `decimal64` and `decimal128`, and a custom 64-bit packing
(`PackedDecimal64`). Every format has a hand-optimised serializer, and the project maps out exactly which
values each one can and can't hold.

**Results:**
- **Fastest overall:** `decimal64` in a packed list serializes and deserializes about **5.5× faster** than
  protobuf-net's built-in decimal, at 8 bytes per value. It holds up to 16 significant digits.
- **Fastest lossless:** `decimal128` in a packed list holds **every** `System.Decimal` exactly and is
  **4–4.5× faster** than the built-in format. The cost is size: 16 bytes per value vs about 10.
- **Smallest:** `PackedDecimal64` in a packed list, **40% smaller** than the built-in format for price-like
  data and about 3.5× faster. It holds up to 17 digits.
- **The built-in format is already as fast as its wire format allows.** A hand-written serializer producing
  identical bytes was no faster. Its cost comes from the format itself (a nested message of three varints),
  not from protobuf-net's implementation.

## Contents

- [Background: what a System.Decimal is](#background-what-a-systemdecimal-is)
- [What was compared](#what-was-compared)
- [Wire size: why the formats differ](#wire-size-why-the-formats-differ)
- [Results](#results)
- [Boundaries](#boundaries)
- [Which one to use](#which-one-to-use)
- [In a `.proto` file, and from Python](#in-a-proto-file-and-from-python)
- [How the serializers are optimised](#how-the-serializers-are-optimised)
- [Running it](#running-it)

## Background: what a System.Decimal is

A `System.Decimal` is 128 bits: a **96-bit unsigned integer mantissa**, a **sign**, and a **scale of 0–28**.
The value is `±mantissa / 10^scale`. So:

- the largest value is ±79,228,162,514,264,337,593,543,950,335 (2^96 − 1), 28–29 significant digits;
- the smallest step is 0.0000000000000000000000000001 (10^-28);
- the scale is kept: `1.10m` and `1.1m` are equal but print differently.

Nothing 64-bit can hold all of that. Every 64-bit format trades away some range, some precision, or both.
IEEE `decimal128` (34 digits) holds all of it.

## What was compared

Every variant is measured end to end (`decimal[]` → message → bytes, and back), so conversion cost is included.
Every conversion **throws** rather than silently rounding when a value doesn't fit.

| Variant | Wire type | How the value is stored |
|---|---|---|
| **Native** | nested message, 3 varints | protobuf-net's built-in `bcl.Decimal`: `lo` (low 64 bits of the mantissa), `hi` (high 32 bits), `signScale` (`scale << 1 \| sign`) |
| **BclDecimalFast** | same as Native | hand-written serializer, byte-for-byte identical output |
| **String** | `string` | invariant culture, e.g. `"-98765.4321"` |
| **Level300** | `string` | protobuf-net 3's `CompatibilityLevel.Level300` default for `decimal`: identical bytes to String |
| **Utf8Decimal** | `string` | formatted/parsed straight to/from UTF-8 on the stack, identical bytes to String |
| **UnitsNanos** | nested message, 2 varints | the `google.type.Money` layout: `int64 units` + `int32 nanos` (billionths) |
| **FixedDecimal** | `sint64` (zigzag varint) | fixed-point: value × 10^8 in a `long` |
| **Decimal64** | `fixed64` | IEEE 754-2008 `decimal64`, BID (binary integer decimal) encoding |
| **PackedDecimal64** | `sint64` (zigzag varint) | custom: 59-bit signed mantissa + 5-bit scale, `mantissa << 5 \| scale` |
| **Decimal128** | `bytes` (16) | IEEE 754-2008 `decimal128`, BID encoding, little-endian |
| **… packed** | packed repeated field | the same numbers as one packed list: one tag and length for the whole field instead of a tag per value. Decimal128 packed is two `fixed64` words per value |

## Wire size: why the formats differ

Protobuf writes most integers as **varints**: 7 bits per byte, so small numbers take few bytes and large
numbers take up to 10. Each format pays a different amount of **framing** per value, and puts a different
**number** on the wire. Together those decide the size.

Take `123.45` (mantissa 12345, scale 2) as one item in a list:

| Format | Bytes | Framing | The number(s) written | Total |
|---|---|---:|---|---:|
| Native | `0A 05` `08 B960` `18 04` | 4 bytes: list tag, length, 2 field tags | mantissa 12345 (2 bytes) + signScale 4 (1 byte) | 7 |
| FixedDecimal | `08` `80818EFD5B` | 1 byte: list tag | 12,345,000,000 (scaled up to 8 dp, 5 bytes) | 6 |
| Decimal64 | `09` `3930000000008031` | 1 byte | always 8 bytes | 9 |
| PackedDecimal64 | `08` `C49C30` | 1 byte | 12345 << 5 \| 2 = 395,042 (3 bytes after zigzag) | **4** |

- **The built-in format isn't a 64-bit format.** It's lossless for the full 96-bit mantissa, so it has no
  fixed size. It writes the raw mantissa as a varint (small for small numbers) but pays 4 bytes of framing,
  because each value is a nested message with its own length and field tags.
- **FixedDecimal** has only 1 byte of framing, but always scales the value up to 8 decimal places. That adds
  up to 8 digits (about 27 bits, 3–4 varint bytes) to every value that has fewer decimal places.
- **PackedDecimal64** has 1 byte of framing and adds only 6 bits to the raw mantissa (5 for the scale, 1 for
  the zigzag sign), which is why it's the smallest.
- **Decimal64** is always 8 bytes of payload, whatever the value.
- **Packed lists** remove the 1-byte tag per value (one tag and length cover the whole list): exactly the
  997-byte saving for each 64-bit format below. Decimal128 saves 1,997, because as two packed `fixed64` words
  it also drops the length byte each 16-byte `bytes` value needed. Nested messages and strings can't be packed.

For the benchmark data (1,000 price-like values: up to 10 digits, 0–8 decimal places, either sign):

| Format | Bytes | Per value | Smallest – largest per value ¹ |
|---|---:|---:|---|
| **PackedDecimal64 packed** | **5,939** | **5.9** | 1 – 10 |
| PackedDecimal64 | 6,936 | 6.9 | 2 – 11 |
| FixedDecimal packed | 7,029 | 7.0 | 1 – 10 |
| FixedDecimal | 8,026 | 8.0 | 2 – 11 |
| Decimal64 packed | 8,003 | 8.0 | 8 |
| Decimal64 | 9,000 | 9.0 | 9 |
| Native / BclDecimalFast | 9,884 | 9.9 | 2 – 21 |
| String / Level300 / Utf8Decimal | 13,286 | 13.3 | 3 – 33 |
| Decimal128 packed | 16,003 | 16.0 | 16 |
| UnitsNanos | 16,895 | 16.9 | 2 – 24 ² |
| Decimal128 | 18,000 | 18.0 | 18 |

¹ Any value the format can hold, not just this data. Zero is the smallest in every varint format.
² `units` and `nanos` are plain `int64`/`int32` varints, which sign-extend to 10 bytes each for negative values.
The other varint formats use zigzag `sint64` to avoid this.

## Results

Intel Core i7-6700K, Windows 10 22H2, .NET 10.0.8, BenchmarkDotNet 0.15.8, protobuf-net 3.4.30.
3 warmup + 15 measured iterations, 1,000 values. Full table: [`results/DecimalBenchmarks.md`](results/DecimalBenchmarks.md).

> **Noise:** the machine wasn't idle, and error bars are roughly ±10–15%. Treat gaps under ~20% as ties.
> Compare within this table; absolute numbers moved between runs (Native was 79 µs to serialize in an
> earlier run and 90 µs in this one).

| Format | Serialize | vs Native | Deserialize | vs Native | Serialize alloc | Deserialize alloc |
|---|---:|---:|---:|---:|---:|---:|
| Native | 90 µs | 1.00 | 116 µs | 1.00 | 16 KB | 32 KB |
| BclDecimalFast | 89 µs | 0.99 | 111 µs | 0.97 | 16 KB | 32 KB |
| String | 135 µs | 1.53 | 202 µs | 1.76 | 56 KB | 72 KB |
| Level300 | 178 µs | 2.01 | 130 µs | 1.13 | 16 KB | 32 KB |
| Utf8Decimal | 111 µs | 1.25 | 171 µs | 1.49 | 16 KB | 32 KB |
| UnitsNanos | 120 µs | 1.36 | 117 µs | 1.02 | 16 KB | 32 KB |
| FixedDecimal | 35 µs | 0.39 | 56 µs | 0.49 | 8 KB | 24 KB |
| FixedDecimal packed | 33 µs | 0.37 | 39 µs | 0.34 | 8 KB | 24 KB |
| Decimal64 | 23 µs | 0.26 | 32 µs | 0.28 | 8 KB | 24 KB |
| **Decimal64 packed** | **16 µs** | **0.18** | **20 µs** | **0.18** | 8 KB | 24 KB |
| PackedDecimal64 | 28 µs | 0.31 | 51 µs | 0.44 | 8 KB | 24 KB |
| PackedDecimal64 packed | 23 µs | 0.26 | 35 µs | 0.31 | 8 KB | 24 KB |
| Decimal128 | 32 µs | 0.36 | 62 µs | 0.54 | 16 KB | 32 KB |
| **Decimal128 packed** | **20 µs** | **0.23** | **29 µs** | **0.25** | 16 KB | 32 KB |

Conversion alone (`decimal` → format → `decimal`, 1,000 values, no protobuf):

| Format | Now | First version ³ |
|---|---:|---:|
| Decimal64 | 8.4 µs | 17 µs |
| Decimal128 | 9.4 µs | – |
| FixedDecimal | 11.8 µs | 23 µs |
| PackedDecimal64 | 12.1 µs | 11 µs |
| UnitsNanos | 19.8 µs | 103 µs |

³ The first version used `decimal.GetBits` (and `decimal` arithmetic for UnitsNanos). See
[How the serializers are optimised](#how-the-serializers-are-optimised).

**Findings:**

1. **Packed lists are the biggest single win for the 64-bit formats:** a third faster to deserialize and a byte
   smaller per value. protobuf-net reads and writes packed `long[]`/`ulong[]` in bulk, whereas custom scalar
   types go through the serializer once per value. (protobuf-net ignores `IsPacked` on custom scalar types, so
   packed lists here are raw `long[]`/`ulong[]` plus the same converters.)
2. **Decimal64 packed is the fastest format:** about 5.5× the built-in format in both directions. Fixed
   8-byte values need no varint encoding at all.
3. **Decimal128 packed is the fastest lossless format:** it holds every `System.Decimal`, round-trips the
   scale, and is still 4–4.5× faster than the built-in format. Its conversion is a few shifts, because a
   decimal128 coefficient can hold the 96-bit mantissa as-is.
4. **The built-in format can't be made meaningfully faster:** `BclDecimalFast` writes identical bytes with a
   hand-written, self-measuring serializer and lands within noise of it. The cost is the wire format.
5. **Strings stay slow however they're written.** Formatting and parsing text is the cost. The allocation-free
   versions (Level300, Utf8Decimal) remove the garbage but not most of the time.
6. **UnitsNanos improved a lot with integer-only conversion** (5× faster to convert) and a hand-written
   serializer, reaching roughly built-in speed. It's still a nested message and the largest varint format.
7. **The 64-bit formats allocate half as much when serializing:** their `List<T>` or array holds 8-byte items
   instead of 16-byte decimals.

## Boundaries

### What each format can hold

| Format | Significant digits | Largest magnitude | Smallest step | Keeps scale (1.10 vs 1.1) |
|---|---|---|---|---|
| System.Decimal / Native / String | 28–29 | 79,228,162,514,264,337,593,543,950,335 | 10^-28 | yes |
| **Decimal128 (IEEE)** | **34** | 9.99… × 10^6144 ⁴ | 10^-6176 ⁴ | yes |
| UnitsNanos | up to 28 (19 + 9) | 9,223,372,036,854,775,807.999999999 | 10^-9 | no (always 9) |
| FixedDecimal (scale 8) | up to 19, fixed split | 92,233,720,368.54775807 | 10^-8 | no (always 8) |
| Decimal64 (IEEE) | 16 | 9.999999999999999 × 10^384 ⁴ | 10^-398 ⁴ | yes |
| PackedDecimal64 (59+5) | 17 (some 18) | 288,230,376,151,711,743 | 10^-28 | yes ⁵ |

⁴ In `System.Decimal` terms: Decimal128 holds **every** decimal. Decimal64 holds any decimal with **16 or fewer
significant digits** (after dropping trailing zeros). Reading values from other systems back into
`System.Decimal` fails beyond ±7.9 × 10^28, beyond 28 decimal places, or for infinity/NaN.
⁵ Unless trailing zeros had to be dropped to fit the mantissa.

### Measured

Every value below was round-tripped through real protobuf serialization in each format (`--check` prints this).
`ok,s=N` means the value came back equal but with scale N. `overflow` and `too precise` mean the conversion
threw.

| Value | Native | String | UnitsNanos | FixedDecimal | Decimal64 | Packed64 | Decimal128 |
|---|---|---|---|---|---|---|---|
| 0 | ok | ok | ok,s=9 | ok,s=8 | ok | ok | ok |
| 1.10 | ok | ok | ok,s=9 | ok,s=8 | ok | ok | ok |
| 0.00000001 (10^-8) | ok | ok | ok,s=9 | ok | ok | ok | ok |
| 0.000000001 (10^-9) | ok | ok | ok | too precise | ok | ok | ok |
| 0.0000000001 (10^-10) | ok | ok | too precise | too precise | ok | ok | ok |
| 10^-28 | ok | ok | too precise | too precise | ok | ok | ok |
| 0.3333333333333333333333333333 | ok | ok | too precise | too precise | too precise | too precise | ok |
| 9,999,999,999,999,999 (16 digits) | ok | ok | ok,s=9 | overflow | ok | ok | ok |
| 99,999,999,999,999,999 (17 digits) | ok | ok | ok,s=9 | overflow | too precise | ok | ok |
| 288,230,376,151,711,743 (2^58 − 1) | ok | ok | ok,s=9 | overflow | too precise | ok | ok |
| 288,230,376,151,711,744 (2^58) | ok | ok | ok,s=9 | overflow | too precise | overflow | ok |
| 92,233,720,368.54775807 | ok | ok | ok,s=9 | ok | too precise | too precise | ok |
| 92,233,720,368.54775808 | ok | ok | ok,s=9 | overflow | too precise | too precise | ok |
| 9,223,372,036,854,775,807 | ok | ok | ok,s=9 | overflow | too precise | overflow | ok |
| 9,223,372,036,854,775,807.999999999 | ok | ok | ok | too precise | too precise | too precise | ok |
| 10^20 | ok | ok | overflow | overflow | ok | overflow | ok |
| decimal.MaxValue | ok | ok | overflow | overflow | too precise | overflow | ok |
| decimal.MinValue | ok | ok | overflow | overflow | too precise | overflow | ok |

### Choosing your own boundaries

**FixedDecimal:** pick the scale for your domain. A signed 64-bit integer holds 18–19 digits in total, so every
decimal place you add costs a digit of range:

| Scale | Largest value | Typical use |
|---:|---|---|
| 0 | ±9,223,372,036,854,775,807 | whole units / minor units already applied |
| 2 | ±92,233,720,368,547,758.07 | cents |
| 4 | ±922,337,203,685,477.5807 | SQL Server `money` uses exactly this |
| 6 | ±9,223,372,036,854.775807 | micro-units |
| **8** | **±92,233,720,368.54775807** | **this benchmark; Bitcoin (satoshis)** |
| 9 | ±9,223,372,036.854775807 | the precision of `google.type.Money` |
| 12 | ±9,223,372.036854775807 | |
| 18 | ±9.223372036854775807 | Ethereum-style 18-decimal tokens (too small for balances) |

**PackedDecimal64:** the split between mantissa and scale bits is a choice too. Scale above 28 is useless for
`System.Decimal`, so 5 bits is the natural fit:

| Scale bits | Scale range | Largest mantissa |
|---:|---|---|
| 4 | 0–15 | ±576,460,752,303,423,487 |
| **5** | **0–31 (0–28 used)** | **±288,230,376,151,711,743** |
| 6 | 0–63 | ±144,115,188,075,855,871 |

**Decimal64 and Decimal128** have fixed boundaries: they're standards.

## Which one to use

| If your values... | Use | Why |
|---|---|---|
| can be anything a `System.Decimal` can hold, and speed matters | **Decimal128, packed** | Lossless, keeps scale, a standard, 4–4.5× faster than built-in; 16 bytes per value |
| can be anything, and size matters more than speed | **Native** (built-in) | Lossless, about 10 bytes for typical values, no custom code |
| are general decimals of up to 16 significant digits | **Decimal64, packed** | The fastest format, a standard, keeps scale, 8 bytes per value |
| have a known number of decimal places and a known maximum (money in one currency, prices) | **FixedDecimal, packed** | Just an integer in every language; sorts and sums natively |
| need up to 17 digits and the smallest wire size | **PackedDecimal64, packed** | Smallest on the wire, keeps scale, but a custom format every reader needs a decoder for |
| must follow Google's API conventions | **UnitsNanos** (`google.type.Money`) or String (`google.type.Decimal`) | Compatibility; accept the size and speed cost |

GCC's `_Decimal64` and `_Decimal128` use the same BID encoding on x86-64, and Intel's Decimal Floating-Point
Math Library reads it too.

**Caveats that apply to all the custom formats:**
- They throw rather than round. If you'd rather round, round explicitly first (for example
  `decimal.Round(value, 8)` before `FixedDecimal.FromDecimal`) so it's a visible decision.
- Negative zero (`-0m`) loses its sign in FixedDecimal and PackedDecimal64. It's still equal to zero.
- FixedDecimal and UnitsNanos don't keep the scale. Anything that compares with `ToString()`, or displays the raw
  value, will see `1.10000000` instead of `1.10`.
- Decimal128's 16-byte layout here is little-endian (low 64 bits first). IEEE 754 doesn't fix a byte order for
  interchange, so state it in the `.proto` comment, as below.

## In a `.proto` file, and from Python

[`proto/decimal_example.proto`](proto/decimal_example.proto) declares every format:

```proto
// protobuf-net's built-in System.Decimal: value = ±(hi << 64 | lo) / 10^scale, sign_scale = scale << 1 | sign.
message BclDecimal {
  uint64 lo = 1;
  uint32 hi = 2;
  uint32 sign_scale = 3;
}

// google.type.Money's number part: value = units + nanos / 10^9, same sign.
message UnitsNanos {
  int64 units = 1;
  int32 nanos = 2;
}

message Samples {
  repeated BclDecimal native = 1;
  repeated string text = 2;
  repeated UnitsNanos units_nanos = 3;
  repeated sint64 fixed_decimal = 4;      // value × 10^8
  repeated fixed64 decimal64 = 5;         // IEEE 754-2008 decimal64, BID encoding
  repeated sint64 packed_decimal64 = 6;   // mantissa << 5 | scale
  repeated bytes decimal128 = 7;          // IEEE 754-2008 decimal128, BID encoding, 16 bytes little-endian
}
```

The declared type matters: `sint64` (not `int64`) for the zigzag-encoded formats, and `fixed64` for Decimal64.
In proto3, repeated `sint64`/`fixed64` fields are packed by default, which matches the packed variants.

### Python ✅ verified

[`interop/python/read_samples.py`](interop/python/read_samples.py) decodes all seven formats from a file written
by .NET (`--export`) and checks them against the expected values. All seven pass. FixedDecimal and UnitsNanos
come back with 8 and 9 decimal places, as expected.

```python
from decimal import Decimal

def from_fixed_decimal(raw: int) -> Decimal:          # sint64
    return Decimal(raw).scaleb(-8)

def from_packed_decimal64(raw: int) -> Decimal:       # sint64
    return Decimal(raw >> 5).scaleb(-(raw & 31))

def from_decimal64(bits: int) -> Decimal:             # fixed64, IEEE 754 BID
    sign = bits >> 63
    if (bits >> 61) & 0b11 == 0b11:                   # large-coefficient form
        if (bits >> 59) & 0b11 == 0b11:
            raise ValueError("infinity or NaN")
        exponent = (bits >> 51) & 0x3FF
        coefficient = (1 << 53) | (bits & ((1 << 51) - 1))
        if coefficient > 9_999_999_999_999_999:
            coefficient = 0                           # non-canonical = zero
    else:
        exponent = (bits >> 53) & 0x3FF
        coefficient = bits & ((1 << 53) - 1)
    return Decimal((sign, tuple(map(int, str(coefficient))), exponent - 398))

def from_decimal128(raw: bytes) -> Decimal:           # bytes, IEEE 754 BID, little-endian
    bits = int.from_bytes(raw, "little")
    sign = bits >> 127
    if (bits >> 125) & 0b11 == 0b11:
        if (bits >> 123) & 0b11 == 0b11:
            raise ValueError("infinity or NaN")
        return Decimal((sign, (0,), 0))               # always non-canonical = zero
    exponent = (bits >> 113) & 0x3FFF
    coefficient = bits & ((1 << 113) - 1)
    if coefficient > 10**34 - 1:
        coefficient = 0
    return Decimal((sign, tuple(map(int, str(coefficient))), exponent - 6176))

def from_bcl(m) -> Decimal:                           # protobuf-net's built-in decimal
    mantissa = (m.hi << 64) | m.lo
    return Decimal((m.sign_scale & 1, tuple(map(int, str(mantissa))), -(m.sign_scale >> 1)))
```

## How the serializers are optimised

- **Direct access to the decimal's bits.** `System.Decimal` is stored as flags (sign and scale), the high 32
  bits of the mantissa, then the low 64 bits. The converters reinterpret it as that struct (`Unsafe.As`)
  instead of calling `decimal.GetBits`, and build results the same way. That's an implementation detail, so
  `DecimalParts.VerifyLayout()` checks it against `GetBits` at startup and refuses to run if it ever changes.
- **Fast path, slow path.** Each converter handles the common case (mantissa fits 64 bits, scale in range)
  with a few integer operations, and only falls back to 128-bit arithmetic for unusual values.
- **Integer-only UnitsNanos.** Units and nanos come from one division and one multiplication, not `decimal`
  arithmetic.
- **Self-measuring serializers** (`IMeasuringSerializer<T>`) for the nested-message formats (BclDecimalFast,
  UnitsNanos), so protobuf-net doesn't have to write each value twice to learn its length.
- **Stack buffers** for `bytes`/`string` payloads (Decimal128, Utf8Decimal): no `byte[]` or `string` per value.
- **Packed lists** via raw `long[]`/`ulong[]`, because protobuf-net doesn't pack custom scalar types.

## Running it

```bash
cd DecimalSerialization
dotnet run -c Release -- --check               # wire bytes, byte-identity checks, round trips, boundary matrix
dotnet run -c Release -- --filter '*'          # all benchmarks (~10 minutes)
dotnet run -c Release -- --export samples.bin  # every format side by side, for other languages
```

Python interop test (from `interop/python`):

```bash
pip install grpcio-tools
python -m grpc_tools.protoc -I ../../proto --python_out=. ../../proto/decimal_example.proto
dotnet run -c Release --project ../.. -- --export samples.bin
python read_samples.py samples.bin
```

| File | Contents |
|---|---|
| [`Formats.cs`](Formats.cs) | Every format's type, converter and serializer, plus the decimal layout access |
| [`Contracts.cs`](Contracts.cs) | Message types (including packed variants) and `decimal[]` mapping |
| [`Benchmarks.cs`](Benchmarks.cs) | Convert / serialize / deserialize benchmarks |
| [`Program.cs`](Program.cs) | Wire-format and byte-identity checks, boundary matrix, `--export`, then BenchmarkDotNet |
| [`proto/decimal_example.proto`](proto/decimal_example.proto) | How to declare each format for other languages |
| [`interop/python/read_samples.py`](interop/python/read_samples.py) | .NET → Python check for all seven formats |
