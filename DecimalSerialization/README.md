# Decimal serialization with protobuf-net: decimal, fixed-point and 64-bit decimals

How should a `System.Decimal` go over the wire with [protobuf-net](https://github.com/protobuf-net/protobuf-net)?
This project compares protobuf-net's built-in format, strings, Google's units+nanos layout, a fixed-point
integer (`FixedDecimal`), IEEE 754 `decimal64`, and a custom 64-bit packing (`PackedDecimal64`), and maps out
exactly which values each one can and can't hold.

**Result:** if your values fit in 64 bits, the 64-bit formats serialize **1.7–3× faster** and deserialize
**about 2× faster** than protobuf-net's built-in decimal. They're **up to 30% smaller** on the wire and
allocate half as much when serializing. They can't hold everything a
`System.Decimal` can, so the choice comes down to which [boundaries](#boundaries) you can live with.

## Contents

- [Background: what a System.Decimal is](#background-what-a-systemdecimal-is)
- [What was compared](#what-was-compared)
- [Wire format](#wire-format)
- [Results](#results)
- [Boundaries](#boundaries)
- [Which one to use](#which-one-to-use)
- [In a `.proto` file, and from Python](#in-a-proto-file-and-from-python)
- [Running it](#running-it)

## Background: what a System.Decimal is

A `System.Decimal` is 128 bits: a **96-bit unsigned integer mantissa**, a **sign**, and a **scale of 0–28**.
The value is `±mantissa / 10^scale`. So:

- the largest value is ±79,228,162,514,264,337,593,543,950,335 (2^96 − 1), 28–29 significant digits;
- the smallest step is 0.0000000000000000000000000001 (10^-28);
- the scale is kept: `1.10m` and `1.1m` are equal but print differently.

Nothing 64-bit can hold all of that. Every 64-bit format trades away some range, some precision, or both.

## What was compared

Every variant is measured end to end (`decimal[]` → message → bytes, and back), so conversion cost is included.
Every conversion **throws** rather than silently rounding when a value doesn't fit.

| Variant | Wire type | How the value is stored |
|---|---|---|
| **Native** | nested message, 3 varints | protobuf-net's `bcl.Decimal`: `lo` (low 64 bits of mantissa), `hi` (high 32 bits), `signScale` (`scale << 1 \| sign`) |
| **String** | `string` | invariant culture, e.g. `"-98765.4321"` |
| **Level300** | `string` | protobuf-net 3's `CompatibilityLevel.Level300` default for `decimal`: byte-for-byte the same as String |
| **UnitsNanos** | nested message, 2 varints | the `google.type.Money` layout: `int64 units` + `int32 nanos` (billionths) |
| **FixedDecimal** | `sint64` (zigzag varint) | fixed-point: value × 10^8 in a `long` |
| **Decimal64** | `fixed64` (8 bytes) | IEEE 754-2008 `decimal64`, BID (binary integer decimal) encoding |
| **PackedDecimal64** | `sint64` (zigzag varint) | custom: 59-bit signed mantissa + 5-bit scale, `mantissa << 5 \| scale` |

The last three are custom types with their own protobuf-net scalar serializers, in [`Formats.cs`](Formats.cs).

## Wire format

For `123.45` as one item of a repeated field, from [`results/wire-format-check.txt`](results/wire-format-check.txt):

| Variant | Bytes | Size | Decoded |
|---|---|---:|---|
| Native | `0A05` `08 B960` `18 04` | 7 | lo = 12345, signScale = 4 (scale 2, positive) |
| String / Level300 | `0A06` `"123.45"` | 8 | |
| UnitsNanos | `0A08` `08 7B` `10 80E9C9D601` | 10 | units = 123, nanos = 450,000,000 |
| FixedDecimal | `08` `80818EFD5B` | 6 | 12,345,000,000 (= 123.45 × 10^8) |
| Decimal64 | `09` `3930000000008031` | 9 | coefficient 12345, exponent −2 |
| PackedDecimal64 | `08` `C49C30` | 4 | 12345 << 5 \| 2 = 395,042 |

For the benchmark data (1,000 price-like values: up to 10 digits, 0–8 decimal places, either sign):

| Variant | Bytes | Per value |
|---|---:|---:|
| **PackedDecimal64** | **6,936** | **6.9** |
| FixedDecimal | 8,026 | 8.0 |
| Decimal64 | 9,000 | 9.0 (always) |
| Native | 9,884 | 9.9 |
| String / Level300 | 13,286 | 13.3 |
| UnitsNanos | 16,895 | 16.9 |

The varint formats (Packed, FixedDecimal) depend on magnitude: small values like `1.5` take 2–3 bytes.
FixedDecimal pays for always scaling up to 8 decimal places. Decimal64 is a fixed 9 bytes.

## Results

Intel Core i7-6700K, Windows 10 22H2, .NET 10.0.8, BenchmarkDotNet 0.15.8, protobuf-net 3.4.30.
3 warmup + 15 measured iterations, 1,000 values. Full table: [`results/DecimalBenchmarks.md`](results/DecimalBenchmarks.md).

> **Noise:** the machine wasn't idle, and error bars are roughly ±10–20%. Treat gaps under ~20% as ties.

| Variant | Serialize | Deserialize | Convert only ¹ | Serialize alloc | Deserialize alloc |
|---|---:|---:|---:|---:|---:|
| Native | 79 µs | 80 µs | – | 16 KB | 32 KB |
| **Decimal64** | **26 µs** | **34 µs** | 17 µs | **8 KB** | **24 KB** |
| **PackedDecimal64** | **31 µs** | **44 µs** | **11 µs** | **8 KB** | **24 KB** |
| FixedDecimal | 47 µs | 44 µs | 23 µs | 8 KB | 24 KB |
| String | 126 µs | 195 µs | 192 µs | 56 KB | 72 KB |
| Level300 | 136 µs | 117 µs | – | 16 KB | 32 KB |
| UnitsNanos | 162 µs | 143 µs | 103 µs | 16 KB | 32 KB |

¹ `decimal` → representation → `decimal` for 1,000 values, no protobuf involved.

**Findings:**

1. **The 64-bit scalars win on every measure.** Each value is a single field with no nested message to size
   first, and the conversions are integer arithmetic. Their `List<T>` is half the size of a `List<decimal>`
   (8-byte vs 16-byte items), which is where the halved allocation comes from.
2. **Decimal64 is the fastest to serialize and deserialize**, because it writes a fixed 8 bytes with no varint
   encoding. **PackedDecimal64 has the cheapest conversion and the smallest wire size**, because small values
   become short varints.
3. **Native is middling:** a nested message of three varints costs sizing work on every value and ends up
   bigger than Packed.
4. **Strings are the slowest plain option**, about 1.3× Native's size and 1.6–2.5× slower. Level300's string
   mode avoids allocating the strings but isn't faster.
5. **UnitsNanos is the slowest and the largest.** It's a nested message, `nanos` is a 4–5 byte varint
   whenever there's a fractional part, and for negative values both fields take 10 bytes (plain `int64`/`int32`
   varints sign-extend to 64 bits, which is why the other formats use zigzag `sint64`). Splitting into units and nanos needs `decimal` arithmetic (about
   100 µs per 1,000 values). Its advantage is that it's a Google standard (`google.type.Money`).

## Boundaries

### What each format can hold

| Format | Bits | Significant digits | Largest magnitude | Smallest step | Keeps scale (1.10 vs 1.1) |
|---|---:|---|---|---|---|
| System.Decimal / Native / String | 96 + sign + scale | 28–29 | 79,228,162,514,264,337,593,543,950,335 | 10^-28 | yes |
| UnitsNanos | 64 + 32 | up to 28 (19 + 9) | 9,223,372,036,854,775,807.999999999 | 10^-9 | no |
| FixedDecimal (scale 8) | 64 | up to 19, fixed split | 92,233,720,368.54775807 | 10^-8 | no (always 8) |
| Decimal64 (IEEE) | 64 | 16 | 9.999999999999999 × 10^384 ² | 10^-398 ² | yes |
| PackedDecimal64 (59+5) | 64 | 17 (some 18) | 288,230,376,151,711,743 | 10^-28 | yes ³ |

² In `System.Decimal` terms, Decimal64 holds any value with **16 or fewer significant digits** (after dropping
trailing zeros), anywhere in the decimal range. The reverse direction fails for values beyond ±7.9 × 10^28,
values needing more than 28 decimal places, and infinity or NaN.
³ Unless trailing zeros had to be dropped to fit the mantissa.

### Measured

Every value below was round-tripped through real protobuf serialization in each format (`--check` prints this).
`ok,s=N` means the value came back equal but with scale N. `overflow` and `too precise` mean the conversion
threw.

| Value | Native | String | UnitsNanos | FixedDecimal | Decimal64 | Packed64 |
|---|---|---|---|---|---|---|
| 0 | ok | ok | ok | ok,s=8 | ok | ok |
| 1.10 | ok | ok | ok,s=1 | ok,s=8 | ok | ok |
| 0.00000001 (10^-8) | ok | ok | ok | ok | ok | ok |
| 0.000000001 (10^-9) | ok | ok | ok | too precise | ok | ok |
| 0.0000000001 (10^-10) | ok | ok | too precise | too precise | ok | ok |
| 10^-28 | ok | ok | too precise | too precise | ok | ok |
| 0.3333333333333333333333333333 | ok | ok | too precise | too precise | too precise | too precise |
| 9,999,999,999,999,999 (16 digits) | ok | ok | ok | overflow | ok | ok |
| 99,999,999,999,999,999 (17 digits) | ok | ok | ok | overflow | too precise | ok |
| 288,230,376,151,711,743 (2^58 − 1) | ok | ok | ok | overflow | too precise | ok |
| 288,230,376,151,711,744 (2^58) | ok | ok | ok | overflow | too precise | overflow |
| 92,233,720,368.54775807 | ok | ok | ok | ok | too precise | too precise |
| 92,233,720,368.54775808 | ok | ok | ok | overflow | too precise | too precise |
| 9,223,372,036,854,775,807 | ok | ok | ok | overflow | too precise | overflow |
| 9,223,372,036,854,775,807.999999999 | ok | ok | ok | too precise | too precise | too precise |
| 10^20 | ok | ok | overflow | overflow | ok | overflow |
| decimal.MaxValue | ok | ok | overflow | overflow | too precise | overflow |
| decimal.MinValue | ok | ok | overflow | overflow | too precise | overflow |

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

**Decimal64** has fixed boundaries (it's a standard): 16 digits and an exponent of −398 to +369. If you need
every `System.Decimal` value in a standard format, IEEE `decimal128` has 34 digits and holds them all, at
16 bytes per value (not benchmarked here).

## Which one to use

| If your values... | Use | Why |
|---|---|---|
| have a known number of decimal places and a known maximum (money in one currency, prices) | **FixedDecimal** at the right scale | Just an integer in every language, sorts and sums natively, simplest to explain |
| are general decimals of up to 16 significant digits and other languages will read them | **Decimal64** | A standard (IEEE 754), fastest, keeps scale; GCC's `_Decimal64` uses the same BID encoding on x86-64 |
| need up to 17 digits, the `System.Decimal` scale range, and the smallest wire size | **PackedDecimal64** | Most precision in 64 bits, keeps scale, smallest on the wire, but it's a custom format every reader needs a decoder for |
| can be anything a `System.Decimal` can hold | **Native** | Lossless, and not slow; String if people need to read it, which is also what `google.type.Decimal` uses |
| must follow Google's API conventions for money | **UnitsNanos** | `google.type.Money` compatibility; accept the speed and size cost |

**Caveats that apply to all the custom formats:**
- They throw rather than round. If you'd rather round, round explicitly first (for example
  `decimal.Round(value, 8)` before `FixedDecimal.FromDecimal`) so it's a visible decision.
- Negative zero (`-0m`) loses its sign in FixedDecimal and PackedDecimal64. It's still equal to zero.
- FixedDecimal and UnitsNanos don't keep the scale. Anything that compares with `ToString()`, or displays the raw
  value, will see `1.10000000` instead of `1.10`.

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
}
```

The declared type matters: `sint64` (not `int64`) for the zigzag-encoded formats, and `fixed64` for Decimal64.

### Python ✅ verified

[`interop/python/read_samples.py`](interop/python/read_samples.py) decodes all six formats from a file written by
.NET (`--export`) and checks them against the expected values. All six pass. FixedDecimal and UnitsNanos come
back with 8 and 9 decimal places, as expected.

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
    else:
        exponent = (bits >> 53) & 0x3FF
        coefficient = bits & ((1 << 53) - 1)
    if coefficient > 9_999_999_999_999_999:
        coefficient = 0                               # non-canonical = zero
    return Decimal((sign, tuple(map(int, str(coefficient))), exponent - 398))

def from_bcl(m) -> Decimal:                           # protobuf-net's built-in decimal
    mantissa = (m.hi << 64) | m.lo
    return Decimal((m.sign_scale & 1, tuple(map(int, str(mantissa))), -(m.sign_scale >> 1)))
```

## Running it

```bash
cd DecimalSerialization
dotnet run -c Release -- --check               # wire bytes, round trips, boundary matrix
dotnet run -c Release -- --filter '*'          # all benchmarks (~6 minutes)
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
| [`Formats.cs`](Formats.cs) | `FixedDecimal`, `Decimal64`, `PackedDecimal64`, `UnitsNanos` and their serializers |
| [`Contracts.cs`](Contracts.cs) | Message types and `decimal[]` mapping |
| [`Benchmarks.cs`](Benchmarks.cs) | Convert / serialize / deserialize benchmarks |
| [`Program.cs`](Program.cs) | Wire-format check, boundary matrix, `--export`, then BenchmarkDotNet |
| [`proto/decimal_example.proto`](proto/decimal_example.proto) | How to declare each format for other languages |
| [`interop/python/read_samples.py`](interop/python/read_samples.py) | .NET → Python check for all six formats |
