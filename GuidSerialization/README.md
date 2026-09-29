# Guid serialization with protobuf-net and gRPC

What's the best way to send a `Guid` over gRPC with [protobuf-net](https://github.com/protobuf-net/protobuf-net)?
This project compares the built-in handling, raw bytes in both byte orders, strings, two-`long`
representations, protobuf-net 3's `CompatibilityLevel.Level300` options, and a custom `Uuid` type with its
own serializer.

**Result:** the custom [`Uuid`](Uuid.cs) type wins. It writes a plain 16-byte `bytes` field in standard
RFC 9562 byte order (18 bytes per GUID on the wire), allocates nothing per value, serializes about 2.5× faster
than protobuf-net's built-in Guid, and deserializes at the same speed.

## Contents

- [Background: how a Guid is laid out](#background-how-a-guid-is-laid-out)
- [What was compared](#what-was-compared)
- [Wire format](#wire-format)
- [Results](#results)
- [Findings](#findings)
- [The `Uuid` type](#the-uuid-type)
- [Running it](#running-it)

## Background: how a Guid is laid out

`System.Guid` is a 16-byte struct: `int _a; short _b; short _c; byte _d.._k`. On little-endian hardware
(x86, x64, ARM64) the first three fields are stored byte-swapped relative to how the string prints:

| Guid `00112233-4455-6677-8899-aabbccddeeff` | Bytes |
|---|---|
| String / RFC 9562 order (big-endian) | `00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff` |
| .NET memory / `ToByteArray()` | `33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff` |

RFC 9562, Java's `UUID(msb, lsb)`, Python's `uuid.UUID(bytes=...)` and Postgres all use the big-endian form.
.NET 8+ can produce it with `ToByteArray(bigEndian: true)`, `TryWriteBytes(span, bigEndian: true, out _)`
and `new Guid(span, bigEndian: true)`.

protobuf-net's built-in Guid is `bcl.Guid`, a nested message of two `fixed64` fields (`lo = 1`, `hi = 2`)
holding the .NET memory layout, which protobuf-net's own `bcl.proto` describes as "crazy-endian".

## What was compared

Every variant is measured end to end: `Guid[]` → message → bytes, and bytes → message → `Guid[]`, so the
conversion cost is included.

| Variant | Wire type | Byte order |
|---|---|---|
| **Native** | protobuf-net default `bcl.Guid` (message of 2× fixed64) | .NET memory ("crazy-endian") |
| **NativeBytes** | `bytes`, one `byte[]` per GUID from `ToByteArray()` | .NET memory |
| **BigEndianBytes** | `bytes`, one `byte[]` per GUID from `ToByteArray(bigEndian: true)` | RFC |
| **String** | `string`, `"D"` format | text |
| **MyGuidStruct** | message of 2× `sfixed64`, big-endian values (Java msb/lsb) | values RFC, wire bytes little-endian |
| **MyGuidClass** | same as MyGuidStruct, but a class | same |
| **MyGuidStructCrazy** | message of 2× `sfixed64`, .NET memory reinterpreted | byte-identical to Native |
| **StructCustomSerializer** | MyGuidStruct with a hand-written `IMeasuringSerializer<T>` and register-only conversion | values RFC |
| **L300Default** | `CompatibilityLevel.Level300`, default format → `string` | text |
| **L300FixedSize** | `CompatibilityLevel.Level300` + `DataFormat.FixedSize` → 16-byte `bytes` | RFC |
| **Uuid** | custom scalar serializer → 16-byte `bytes`, no allocation | RFC |

## Wire format

From [`results/wire-format-check.txt`](results/wire-format-check.txt), which the program prints before every
run and which also verifies that every variant round-trips 1,000 random GUIDs:

| Variant | Bytes on the wire for one GUID (as a repeated-field item) | Size |
|---|---|---|
| Native / MyGuidStructCrazy | `0A12` `09`**`3322110055447766`** `11`**`8899AABBCCDDEEFF`** | 20 |
| MyGuidStruct / StructCustomSerializer | `0A12` `09`**`7766554433221100`** `11`**`FFEEDDCCBBAA9988`** | 20 |
| **Uuid** / L300FixedSize / BigEndianBytes | `0A10` **`00112233445566778899AABBCCDDEEFF`** | 18 |
| String / L300Default | `0A24` + 36 ASCII characters | 38 |

Why "big-endian longs" don't give RFC bytes on the wire: `MyGuidStruct.Hi` is the *number*
`0x0011223344556677`, but protobuf always encodes `fixed64` little-endian, so each half comes out reversed.
Every protobuf decoder still gets the right numbers back (a Java client can call `new UUID(hi, lo)`), but only
a `bytes` field carries the RFC byte order itself.

## Results

Intel Core i7-6700K, Windows 10 22H2, .NET 10.0.8, BenchmarkDotNet 0.15.8, protobuf-net 3.4.30,
protobuf-net.Grpc 1.3.14. 3 warmup + 15 measured iterations. Full tables are in [`results/`](results/).

> **Noise:** the machine was not idle, and error bars are roughly ±5–20%. Treat differences under ~20% as
> ties. The main effects below are well outside that.

### 1,000 GUIDs per message

| Variant | Serialize (buffer writer) | Serialize (stream) | Deserialize | gRPC round trip | gRPC allocated |
|---|---:|---:|---:|---:|---:|
| Native | 87 µs | 68 µs | 57 µs | 751 µs | 71 KB |
| **Uuid** | **35 µs** | **30 µs** | **54 µs** | **708 µs** | **71 KB** |
| StructCustomSerializer | 87 µs | 54 µs | 79 µs | 883 µs | 71 KB |
| MyGuidStruct | 136 µs | 85 µs | 74 µs | 1,149 µs | 71 KB |
| MyGuidStructCrazy | 141 µs | 81 µs | 86 µs | – | – |
| BigEndianBytes | 48 µs ¹ | – | 79 µs ¹ | 689 µs | 165 KB |
| NativeBytes | 44 µs ¹ | – | 84 µs ¹ | 716 µs | 164 KB |
| String | 89 µs ¹ | – | 156 µs ¹ | 1,220 µs | 329 KB |
| MyGuidClass | 288 µs ¹ | – | 107 µs ¹ | 1,624 µs | 653 KB |
| L300Default | 52 µs | – | 119 µs | – | – |
| L300FixedSize | 208 µs | – | 140 µs | – | – |

¹ From the first run ([`SerializationBenchmarks.md`](results/SerializationBenchmarks.md)); the rest are from
[`StructBenchmarks.md`](results/StructBenchmarks.md) and [`GrpcBenchmarks.md`](results/GrpcBenchmarks.md).
Serialize allocations: 16 KB for every variant without a per-GUID object; 48 KB with a `byte[]` per GUID;
104 KB for String; 171 KB for MyGuidClass.

### 1 GUID per message

Serialize/deserialize is 0.3–0.7 µs for every variant, and a gRPC round trip is 170–190 µs for every variant.
The transport dominates. For a single ID in a request, the representation doesn't matter for speed; pick it
for the wire contract.

### Converting Guid ↔ two big-endian longs (no protobuf), 1,000 GUIDs

| Method | Time | Per GUID |
|---|---:|---:|
| Reinterpret memory ("crazy-endian") | 2.7 µs | ~3 ns |
| `stackalloc` + `TryWriteBytes(bigEndian)` + `BinaryPrimitives` | 23.4 µs | ~23 ns |
| Shifts + `ReverseEndianness`, registers only (`FromFast`/`ToGuidFast`) | 5.2 µs | ~5 ns |

## Findings

1. **Big-endian costs almost nothing.** Written with shifts and `ReverseEndianness`, converting to big-endian
   is about 2.5 ns per GUID more than reinterpreting memory. The naive span-based version is 4–5× slower
   because of the buffer and copying, not because of the byte order.
2. **A `byte[]` per GUID more than doubles memory.** A `byte[16]` is a 40-byte heap object on x64, plus an
   8-byte reference in the list. That's the whole difference between `List<Guid>` (16 KB) and `List<byte[]>`
   (48 KB) for 1,000 GUIDs.
3. **Custom structs are slowed by sizing, not by the data.** A nested message needs a length prefix, so on the
   buffer-writer path protobuf-net sizes each GUID's sub-message before writing it. `MyGuidStructCrazy`
   produces exactly the same bytes as Native but is ~60% slower to serialize. Implementing
   `IMeasuringSerializer<T>` (returning a fixed 18) brings it level with Native. The stream path shows a
   much smaller gap, so it appears to handle length prefixes more cheaply.
4. **Classes are the worst option:** ~9× the allocations of Native over gRPC and the slowest everywhere.
5. **Strings are twice the size** and about twice as slow to deserialize. protobuf-net's Level300 string mode
   avoids allocating the strings and is fast to write, but it's still 38 bytes per GUID.
6. **Level300 `FixedSize` has the right wire format but a slow implementation:** identical bytes to `Uuid`,
   about 6× slower to serialize and 2.5× slower to deserialize. The cause wasn't investigated.
7. **`Uuid` gets the best of everything:** a `bytes` field is a single value with a known length, so there's
   no sub-message to size. The payload is a single 16-byte copy through a stack buffer.

## The `Uuid` type

[`Uuid.cs`](Uuid.cs) is self-contained and can be dropped into any protobuf-net project:

```csharp
[ProtoContract]
public sealed class Order
{
    [ProtoMember(1)] public Uuid Id { get; set; }            // bytes id = 1;  16 bytes, RFC order
    [ProtoMember(2)] public List<Uuid> LineIds { get; set; } = [];
}

Guid g = order.Id;   // implicit conversions both ways
order.Id = Guid.NewGuid();
```

- `.proto` equivalent: `bytes id = 1;` Any language reads it as a standard 16-byte UUID.
- Rejects any payload that isn't exactly 16 bytes with a `ProtoException`.
- Verified inside nested messages, where protobuf-net sizes the enclosing message.
- Uses `unsafe` for the stack buffer: protobuf-net's reader and writer are `ref struct`s, so the compiler
  rejects a `stackalloc` span argument (CS8350). A pointer-backed span is accepted, and it's safe because both
  calls copy the bytes before returning. Needs `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

## Running it

```bash
cd GuidSerialization
dotnet run -c Release -- --check                        # round-trip + wire-format check only
dotnet run -c Release -- --filter '*'                   # everything (~20 minutes)
dotnet run -c Release -- --filter '*StructBenchmarks*'  # formats, Level300, Uuid, conversion
dotnet run -c Release -- --filter '*GrpcBenchmarks*'    # gRPC round trips (Kestrel on localhost:50151)
```

| File | Contents |
|---|---|
| [`Uuid.cs`](Uuid.cs) | The custom type and serializer |
| [`Contracts.cs`](Contracts.cs) | All message types, the two-long structs, `Guid[]` mapping, gRPC service |
| [`Benchmarks.cs`](Benchmarks.cs) | `SerializationBenchmarks` (N = 1 and 1,000) and `GrpcBenchmarks` |
| [`StructBenchmarks.cs`](StructBenchmarks.cs) | Conversion, sizing, stream vs buffer, Level300, `Uuid` |
| [`Program.cs`](Program.cs) | Wire-format and round-trip check, then BenchmarkDotNet |
