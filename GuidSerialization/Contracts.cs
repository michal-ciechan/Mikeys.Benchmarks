using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProtoBuf;
using ProtoBuf.Serializers;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace GuidSerialization;

// Two longs holding the RFC 9562 / Java msb+lsb halves (big-endian interpretation).
// On the wire each is sfixed64 (8 bytes) - varint would cost ~10 bytes for random data.
[ProtoContract]
public struct MyGuidStruct
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public long Hi;
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public long Lo;

    public static MyGuidStruct From(Guid g)
    {
        Span<byte> b = stackalloc byte[16];
        g.TryWriteBytes(b, bigEndian: true, out _);
        return new MyGuidStruct { Hi = BinaryPrimitives.ReadInt64BigEndian(b), Lo = BinaryPrimitives.ReadInt64BigEndian(b[8..]) };
    }

    public readonly Guid ToGuid()
    {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(b, Hi);
        BinaryPrimitives.WriteInt64BigEndian(b[8..], Lo);
        return new Guid(b, bigEndian: true);
    }

    // Register-only versions (little-endian hosts). Guid memory = a(int32) b(int16) c(int16) d..k(8 bytes),
    // so the first native long is a | b<<32 | c<<48 and the big-endian Hi is a<<32 | b<<16 | c.
    public static MyGuidStruct FromFast(Guid g)
    {
        var n = Unsafe.As<Guid, MyGuidStructCrazy>(ref g);
        ulong lo = (ulong)n.Lo;
        return new MyGuidStruct
        {
            Hi = (long)((lo << 32) | ((lo >> 16) & 0xFFFF0000UL) | (lo >> 48)),
            Lo = BinaryPrimitives.ReverseEndianness(n.Hi),
        };
    }

    public readonly Guid ToGuidFast()
    {
        ulong hi = (ulong)Hi;
        var n = new MyGuidStructCrazy
        {
            Lo = (long)((hi >> 32) | (((hi >> 16) & 0xFFFF) << 32) | ((hi & 0xFFFF) << 48)),
            Hi = BinaryPrimitives.ReverseEndianness(Lo),
        };
        return Unsafe.As<MyGuidStructCrazy, Guid>(ref n);
    }
}

// Same two sfixed64 fields, but "crazy-endian": the Guid's own memory reinterpreted as two longs.
// Field 1 = first 8 bytes, field 2 = last 8 - byte-for-byte the same wire output as protobuf-net's bcl.Guid.
[ProtoContract]
[StructLayout(LayoutKind.Sequential)]
public struct MyGuidStructCrazy
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public long Lo;
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public long Hi;

    public static MyGuidStructCrazy From(Guid g) => Unsafe.As<Guid, MyGuidStructCrazy>(ref g);
    public Guid ToGuid() => Unsafe.As<MyGuidStructCrazy, Guid>(ref this);
}

// Big-endian longs again, but with a hand-written serializer that also reports its length up front
// (IMeasuringSerializer), so protobuf-net needn't walk the value to size the sub-message.
[ProtoContract(Serializer = typeof(MyGuidStructCustomSerializer))]
public struct MyGuidStructCustom
{
    public long Hi;
    public long Lo;

    // Uses the register-only big-endian conversion - this is the "best two-long" variant.
    public static MyGuidStructCustom From(Guid g)
    {
        var s = MyGuidStruct.FromFast(g);
        return new MyGuidStructCustom { Hi = s.Hi, Lo = s.Lo };
    }
    public readonly Guid ToGuid() => new MyGuidStruct { Hi = Hi, Lo = Lo }.ToGuidFast();
}

public sealed class MyGuidStructCustomSerializer : IMeasuringSerializer<MyGuidStructCustom>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryMessage | SerializerFeatures.WireTypeString;

    public int Measure(ISerializationContext context, WireType wireType, MyGuidStructCustom value) => 18;

    public MyGuidStructCustom Read(ref ProtoReader.State state, MyGuidStructCustom value)
    {
        int field;
        while ((field = state.ReadFieldHeader()) > 0)
        {
            switch (field)
            {
                case 1: value.Hi = state.ReadInt64(); break;
                case 2: value.Lo = state.ReadInt64(); break;
                default: state.SkipField(); break;
            }
        }
        return value;
    }

    public void Write(ref ProtoWriter.State state, MyGuidStructCustom value)
    {
        state.WriteFieldHeader(1, WireType.Fixed64);
        state.WriteInt64(value.Hi);
        state.WriteFieldHeader(2, WireType.Fixed64);
        state.WriteInt64(value.Lo);
    }
}

[ProtoContract]
public sealed class MyGuidClass
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public long Hi { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public long Lo { get; set; }

    public static MyGuidClass From(Guid g)
    {
        Span<byte> b = stackalloc byte[16];
        g.TryWriteBytes(b, bigEndian: true, out _);
        return new MyGuidClass { Hi = BinaryPrimitives.ReadInt64BigEndian(b), Lo = BinaryPrimitives.ReadInt64BigEndian(b[8..]) };
    }

    public Guid ToGuid()
    {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(b, Hi);
        BinaryPrimitives.WriteInt64BigEndian(b[8..], Lo);
        return new Guid(b, bigEndian: true);
    }
}

// protobuf-net's built-in Guid: bcl.Guid { fixed64 lo = 1; fixed64 hi = 2; } in .NET's mixed-endian layout.
[ProtoContract] public sealed class NativeGuidMessage { [ProtoMember(1)] public List<Guid> Ids { get; set; } = []; }

// 16-byte bytes field, Guid.ToByteArray() mixed-endian layout.
[ProtoContract] public sealed class NativeBytesMessage { [ProtoMember(1)] public List<byte[]> Ids { get; set; } = []; }

// 16-byte bytes field, RFC 9562 big-endian layout.
[ProtoContract] public sealed class BigEndianBytesMessage { [ProtoMember(1)] public List<byte[]> Ids { get; set; } = []; }

// 36-char "D" format string.
[ProtoContract] public sealed class StringMessage { [ProtoMember(1)] public List<string> Ids { get; set; } = []; }

[ProtoContract] public sealed class StructMessage { [ProtoMember(1)] public List<MyGuidStruct> Ids { get; set; } = []; }

[ProtoContract] public sealed class ClassMessage { [ProtoMember(1)] public List<MyGuidClass> Ids { get; set; } = []; }

[ProtoContract] public sealed class StructCrazyMessage { [ProtoMember(1)] public List<MyGuidStructCrazy> Ids { get; set; } = []; }

[ProtoContract] public sealed class StructCustomMessage { [ProtoMember(1)] public List<MyGuidStructCustom> Ids { get; set; } = []; }

// Custom ser/de type: repeated bytes, 16 bytes each, RFC order, no per-item allocation.
[ProtoContract] public sealed class UuidMessage { [ProtoMember(1)] public List<Uuid> Ids { get; set; } = []; }

// Nested shape, to prove Uuid survives protobuf-net measuring a sub-message that contains it.
[ProtoContract] public sealed class UuidInner { [ProtoMember(1)] public Uuid Id { get; set; } [ProtoMember(2)] public List<Uuid> More { get; set; } = []; }
[ProtoContract] public sealed class UuidOuter { [ProtoMember(1)] public UuidInner? Inner { get; set; } [ProtoMember(2)] public int After { get; set; } }

// The .NET side of proto/uuid_example.proto's Order message - used by --export/--import for the
// cross-language test in interop/.
[ProtoContract]
public sealed class Order
{
    [ProtoMember(1)] public Uuid Id { get; set; }
    [ProtoMember(2)] public List<Uuid> LineIds { get; set; } = [];
    [ProtoMember(3)] public int Quantity { get; set; }
}

// protobuf-net 3 compatibility level 300: Guid with default data format, and with FixedSize.
[ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
public sealed class L300DefaultMessage { [ProtoMember(1)] public List<Guid> Ids { get; set; } = []; }

[ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
public sealed class L300FixedMessage { [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public List<Guid> Ids { get; set; } = []; }

// Guid[] <-> DTO mapping, so every variant is measured end to end from the same domain type.
public static class Map
{
    public static NativeGuidMessage ToNative(Guid[] ids) => new() { Ids = [.. ids] };
    public static Guid[] From(NativeGuidMessage m) => [.. m.Ids];

    public static NativeBytesMessage ToNativeBytes(Guid[] ids)
    {
        var list = new List<byte[]>(ids.Length);
        foreach (var g in ids) list.Add(g.ToByteArray());
        return new() { Ids = list };
    }
    public static Guid[] From(NativeBytesMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = new Guid(m.Ids[i]);
        return r;
    }

    public static BigEndianBytesMessage ToBigEndian(Guid[] ids)
    {
        var list = new List<byte[]>(ids.Length);
        foreach (var g in ids) list.Add(g.ToByteArray(bigEndian: true));
        return new() { Ids = list };
    }
    public static Guid[] From(BigEndianBytesMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = new Guid(m.Ids[i], bigEndian: true);
        return r;
    }

    public static StringMessage ToStrings(Guid[] ids)
    {
        var list = new List<string>(ids.Length);
        foreach (var g in ids) list.Add(g.ToString());
        return new() { Ids = list };
    }
    public static Guid[] From(StringMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = Guid.ParseExact(m.Ids[i], "D");
        return r;
    }

    public static StructMessage ToStruct(Guid[] ids)
    {
        var list = new List<MyGuidStruct>(ids.Length);
        foreach (var g in ids) list.Add(MyGuidStruct.From(g));
        return new() { Ids = list };
    }
    public static Guid[] From(StructMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = m.Ids[i].ToGuid();
        return r;
    }

    public static UuidMessage ToUuid(Guid[] ids)
    {
        var list = new List<Uuid>(ids.Length);
        foreach (var g in ids) list.Add(g);
        return new() { Ids = list };
    }
    public static Guid[] From(UuidMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = m.Ids[i];
        return r;
    }

    public static L300DefaultMessage ToL300Default(Guid[] ids) => new() { Ids = [.. ids] };
    public static Guid[] From(L300DefaultMessage m) => [.. m.Ids];

    public static L300FixedMessage ToL300Fixed(Guid[] ids) => new() { Ids = [.. ids] };
    public static Guid[] From(L300FixedMessage m) => [.. m.Ids];

    public static StructCrazyMessage ToStructCrazy(Guid[] ids)
    {
        var list = new List<MyGuidStructCrazy>(ids.Length);
        foreach (var g in ids) list.Add(MyGuidStructCrazy.From(g));
        return new() { Ids = list };
    }
    public static Guid[] From(StructCrazyMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = m.Ids[i].ToGuid();
        return r;
    }

    public static StructCustomMessage ToStructCustom(Guid[] ids)
    {
        var list = new List<MyGuidStructCustom>(ids.Length);
        foreach (var g in ids) list.Add(MyGuidStructCustom.From(g));
        return new() { Ids = list };
    }
    public static Guid[] From(StructCustomMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = m.Ids[i].ToGuid();
        return r;
    }

    public static ClassMessage ToClass(Guid[] ids)
    {
        var list = new List<MyGuidClass>(ids.Length);
        foreach (var g in ids) list.Add(MyGuidClass.From(g));
        return new() { Ids = list };
    }
    public static Guid[] From(ClassMessage m)
    {
        var r = new Guid[m.Ids.Count];
        for (int i = 0; i < r.Length; i++) r[i] = m.Ids[i].ToGuid();
        return r;
    }
}

[Service("GuidSerialization.Echo")]
public interface IEchoService
{
    ValueTask<NativeGuidMessage> Native(NativeGuidMessage m, CallContext ctx = default);
    ValueTask<NativeBytesMessage> NativeBytes(NativeBytesMessage m, CallContext ctx = default);
    ValueTask<BigEndianBytesMessage> BigEndian(BigEndianBytesMessage m, CallContext ctx = default);
    ValueTask<StringMessage> String(StringMessage m, CallContext ctx = default);
    ValueTask<StructMessage> Struct(StructMessage m, CallContext ctx = default);
    ValueTask<ClassMessage> Class(ClassMessage m, CallContext ctx = default);
    ValueTask<StructCustomMessage> StructCustom(StructCustomMessage m, CallContext ctx = default);
    ValueTask<UuidMessage> Uuid(UuidMessage m, CallContext ctx = default);
}

public sealed class EchoService : IEchoService
{
    public ValueTask<NativeGuidMessage> Native(NativeGuidMessage m, CallContext ctx = default) => new(m);
    public ValueTask<NativeBytesMessage> NativeBytes(NativeBytesMessage m, CallContext ctx = default) => new(m);
    public ValueTask<BigEndianBytesMessage> BigEndian(BigEndianBytesMessage m, CallContext ctx = default) => new(m);
    public ValueTask<StringMessage> String(StringMessage m, CallContext ctx = default) => new(m);
    public ValueTask<StructMessage> Struct(StructMessage m, CallContext ctx = default) => new(m);
    public ValueTask<ClassMessage> Class(ClassMessage m, CallContext ctx = default) => new(m);
    public ValueTask<StructCustomMessage> StructCustom(StructCustomMessage m, CallContext ctx = default) => new(m);
    public ValueTask<UuidMessage> Uuid(UuidMessage m, CallContext ctx = default) => new(m);
}
