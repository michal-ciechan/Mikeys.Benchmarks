using System.Buffers;
using BenchmarkDotNet.Running;
using GuidSerialization;
using ProtoBuf;

if (args.Contains("--check"))
{
    Check();
    return;
}

Check();
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// Sanity pass before measuring: every variant must round-trip to the same Guid[], and print wire sizes.
static void Check()
{
    var sample = new Guid("00112233-4455-6677-8899-aabbccddeeff");
    Console.WriteLine($"Sample {sample}");
    Console.WriteLine($"  ToByteArray()     {Convert.ToHexString(sample.ToByteArray())}");
    Console.WriteLine($"  ToByteArray(true) {Convert.ToHexString(sample.ToByteArray(bigEndian: true))}");
    var s = MyGuidStruct.From(sample);
    Console.WriteLine($"  MyGuidStruct      Hi=0x{s.Hi:X16} Lo=0x{s.Lo:X16}");
    Console.WriteLine($"  bcl.Guid on wire  {Convert.ToHexString(Bytes(Map.ToNative([sample])))}");
    Console.WriteLine($"  StructBE on wire  {Convert.ToHexString(Bytes(Map.ToStruct([sample])))}");
    Console.WriteLine($"  StructCrazy wire  {Convert.ToHexString(Bytes(Map.ToStructCrazy([sample])))}");
    Console.WriteLine($"  StructCustom wire {Convert.ToHexString(Bytes(Map.ToStructCustom([sample])))}");
    Console.WriteLine($"  L300 default wire {Convert.ToHexString(Bytes(Map.ToL300Default([sample])))}");
    Console.WriteLine($"  L300 fixed wire   {Convert.ToHexString(Bytes(Map.ToL300Fixed([sample])))}");
    Console.WriteLine($"  Uuid on wire      {Convert.ToHexString(Bytes(Map.ToUuid([sample])))}");
    Console.WriteLine();

    // Uuid nested inside a sub-message: protobuf-net has to measure UuidInner, which runs our serializer.
    var outer = new UuidOuter { Inner = new UuidInner { Id = sample, More = [Guid.Empty, sample] }, After = 7 };
    var outerBytes = Bytes(outer);
    var outerBack = Serializer.Deserialize<UuidOuter>((ReadOnlySpan<byte>)outerBytes);
    var inner = outerBack.Inner;
    var nestedOk = inner is not null
        && inner.Id.Value == sample
        && inner.More.Select(u => u.Value).SequenceEqual([Guid.Empty, sample])
        && outerBack.After == 7;
    Console.WriteLine($"  Uuid nested       {Convert.ToHexString(outerBytes)}  {(nestedOk ? "round-trip OK" : "ROUND-TRIP FAILED")}");
    if (!nestedOk) throw new InvalidOperationException("Nested Uuid did not round-trip");

    // A payload that isn't 16 bytes must be rejected, not silently truncated/padded.
    var bad = new byte[] { 0x0A, 0x04, 1, 2, 3, 4 };
    try { Serializer.Deserialize<UuidMessage>((ReadOnlySpan<byte>)bad); Console.WriteLine("  Uuid 4-byte payload ACCEPTED - should have thrown"); }
    catch (Exception ex) { Console.WriteLine($"  Uuid 4-byte payload rejected: {ex.GetType().Name}: {ex.Message}"); }
    Console.WriteLine();

    foreach (var g in Data.Make(10_000).Append(sample))
    {
        var slow = MyGuidStruct.From(g);
        var fast = MyGuidStruct.FromFast(g);
        if (slow.Hi != fast.Hi || slow.Lo != fast.Lo || fast.ToGuidFast() != g)
            throw new InvalidOperationException($"FromFast/ToGuidFast mismatch for {g}");
    }
    Console.WriteLine("  FromFast/ToGuidFast match the BinaryPrimitives version on 10,001 GUIDs");
    Console.WriteLine();

    foreach (var n in new[] { 1, 1000 })
    {
        var ids = Data.Make(n);
        Console.WriteLine($"N={n} wire bytes:");
        Row("Native (bcl.Guid)", Map.ToNative(ids), b => Map.From(Serializer.Deserialize<NativeGuidMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("NativeBytes", Map.ToNativeBytes(ids), b => Map.From(Serializer.Deserialize<NativeBytesMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("BigEndianBytes", Map.ToBigEndian(ids), b => Map.From(Serializer.Deserialize<BigEndianBytesMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("String", Map.ToStrings(ids), b => Map.From(Serializer.Deserialize<StringMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("MyGuidStruct", Map.ToStruct(ids), b => Map.From(Serializer.Deserialize<StructMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("MyGuidClass", Map.ToClass(ids), b => Map.From(Serializer.Deserialize<ClassMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("StructCrazy", Map.ToStructCrazy(ids), b => Map.From(Serializer.Deserialize<StructCrazyMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("StructCustom", Map.ToStructCustom(ids), b => Map.From(Serializer.Deserialize<StructCustomMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("L300Default", Map.ToL300Default(ids), b => Map.From(Serializer.Deserialize<L300DefaultMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("Uuid (custom)", Map.ToUuid(ids), b => Map.From(Serializer.Deserialize<UuidMessage>((ReadOnlySpan<byte>)b)), ids);
        Row("L300FixedSize", Map.ToL300Fixed(ids), b => Map.From(Serializer.Deserialize<L300FixedMessage>((ReadOnlySpan<byte>)b)), ids);
        Console.WriteLine();
    }
}

static void Row<T>(string name, T msg, Func<byte[], Guid[]> back, Guid[] expected)
{
    var bytes = Bytes(msg);
    var ok = back(bytes).SequenceEqual(expected);
    Console.WriteLine($"  {name,-20} {bytes.Length,8:N0}  {(ok ? "round-trip OK" : "ROUND-TRIP FAILED")}");
    if (!ok) throw new InvalidOperationException($"{name} did not round-trip");
}

static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
