using System.Runtime.CompilerServices;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace GuidSerialization;

/// <summary>
/// A Guid that protobuf-net writes as a plain 16-byte <c>bytes</c> field in RFC 9562 (big-endian) order -
/// the same thing any other language's UUID type reads directly. Converts implicitly to and from Guid,
/// so contracts can use it in place of Guid without touching the rest of the code.
/// </summary>
[ProtoContract(Serializer = typeof(UuidSerializer))]
public readonly struct Uuid(Guid value) : IEquatable<Uuid>, IComparable<Uuid>
{
    public Guid Value { get; } = value;

    public static implicit operator Guid(Uuid u) => u.Value;
    public static implicit operator Uuid(Guid g) => new(g);

    public bool Equals(Uuid other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Uuid u && Equals(u);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(Uuid other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString();

    public static bool operator ==(Uuid a, Uuid b) => a.Equals(b);
    public static bool operator !=(Uuid a, Uuid b) => !a.Equals(b);
}

/// <summary>
/// Scalar serializer: the field is <c>bytes</c> (wire type 2), payload exactly 16 bytes, big-endian.
/// Reads and writes through a stack buffer, so there is no byte[] per value.
/// </summary>
public sealed class UuidSerializer : ISerializer<Uuid>
{
    public SerializerFeatures Features => SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

    // The buffer is built from a stackalloc'd pointer rather than `Span<byte> b = stackalloc ...`: the reader and
    // writer are ref structs, so the compiler can't prove they won't hold on to a stack span and rejects it
    // (CS8350). Both calls copy the bytes before returning, so the pointer never outlives the frame.
    [SkipLocalsInit]
    public unsafe Uuid Read(ref ProtoReader.State state, Uuid value)
    {
        byte* p = stackalloc byte[16];
        var read = state.ReadBytes(new Span<byte>(p, 16));
        if (read.Length != 16)
            throw new ProtoException($"Uuid field must be exactly 16 bytes, got {read.Length}");
        return new Uuid(new Guid(read, bigEndian: true));
    }

    [SkipLocalsInit]
    public unsafe void Write(ref ProtoWriter.State state, Uuid value)
    {
        byte* p = stackalloc byte[16];
        var buffer = new Span<byte>(p, 16);
        value.Value.TryWriteBytes(buffer, bigEndian: true, out _);
        state.WriteBytes((ReadOnlySpan<byte>)buffer);
    }
}
