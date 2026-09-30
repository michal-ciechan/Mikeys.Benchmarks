using System.Buffers;
using System.Globalization;
using BenchmarkDotNet.Running;
using DecimalSerialization;
using ProtoBuf;

var inv = CultureInfo.InvariantCulture;
DecimalParts.VerifyLayout();

if (args.Contains("--check"))
{
    Check();
    return;
}

// --export <file>: every format side by side for values all of them can hold (see interop/).
if (args is ["--export", var exportPath])
{
    var values = InteropValues();
    var samples = new Samples
    {
        Native = [.. values],
        Text = [.. values.Select(v => v.ToString(inv))],
        UnitsNanos = Map.ToList<UnitsNanos>(values),
        FixedDecimal = Map.FixedRaw(values),
        Decimal64 = Map.Decimal64Raw(values),
        PackedDecimal64 = Map.PackedRaw(values),
        Decimal128 = Map.ToList<Decimal128>(values),
    };
    File.WriteAllBytes(exportPath, Bytes(samples));
    Console.WriteLine($"Wrote {exportPath}: {string.Join(", ", values.Select(v => v.ToString(inv)))}");
    return;
}

Check();
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

static decimal[] InteropValues() =>
[
    0m, 1m, -1m, 0.1m, 1.10m, 123.45m, -98765.4321m, 0.00000001m, 12345678.9m, 9999999.99999999m,
];

static void Check()
{
    var inv = CultureInfo.InvariantCulture;
    var sample = 123.45m;
    Console.WriteLine($"Sample {sample.ToString(inv)} - one value as a repeated-field item (packed: the whole field):");
    Console.WriteLine($"  Native (bcl)         {Hex(Map.ToNative([sample]))}");
    Console.WriteLine($"  BclDecimalFast       {Hex(Map.ToBclFast([sample]))}");
    Console.WriteLine($"  String / Utf8        {Hex(Map.ToUtf8([sample]))}");
    Console.WriteLine($"  UnitsNanos           {Hex(Map.ToUnitsNanos([sample]))}");
    Console.WriteLine($"  FixedDecimal         {Hex(Map.ToFixed([sample]))}");
    Console.WriteLine($"  FixedDecimal packed  {Hex(Map.ToFixedPacked([sample]))}");
    Console.WriteLine($"  Decimal64            {Hex(Map.ToDecimal64([sample]))}");
    Console.WriteLine($"  Decimal64 packed     {Hex(Map.ToDecimal64Packed([sample]))}");
    Console.WriteLine($"  PackedDecimal64      {Hex(Map.ToPacked([sample]))}");
    Console.WriteLine($"  Packed64 packed      {Hex(Map.ToPackedPacked([sample]))}");
    Console.WriteLine($"  Decimal128           {Hex(Map.ToDecimal128([sample]))}");
    Console.WriteLine($"  Decimal128 packed    {Hex(Map.ToDecimal128Packed([sample]))}");
    Console.WriteLine();

    // The fast serializers must produce exactly the bytes of the formats they replace.
    var data = Data.Make(1000);
    decimal[] extremes = [0m, -0.5m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, 1.10m, 4294967296m * 4294967296m];
    foreach (var set in new[] { data, extremes })
    {
        Same("BclDecimalFast vs built-in", Bytes(Map.ToBclFast(set)), Bytes(Map.ToNative(set)));
        Same("Utf8Decimal vs string", Bytes(Map.ToUtf8(set)), Bytes(Map.ToStrings(set)));
        Same("Level300 vs string", Bytes(Map.ToL300(set)), Bytes(Map.ToStrings(set)));
    }
    Console.WriteLine("  BclDecimalFast, Utf8Decimal and Level300 are byte-identical to the formats they replace");
    Console.WriteLine();

    Console.WriteLine("Benchmark data (1,000 values), wire bytes:");
    Row("Native (bcl)", Map.ToNative(data), b => Map.From(Serializer.Deserialize<NativeMessage>((ReadOnlySpan<byte>)b)), data);
    Row("BclDecimalFast", Map.ToBclFast(data), b => Map.From(Serializer.Deserialize<BclFastMessage>((ReadOnlySpan<byte>)b)), data);
    Row("String", Map.ToStrings(data), b => Map.From(Serializer.Deserialize<StringMessage>((ReadOnlySpan<byte>)b)), data);
    Row("Level300", Map.ToL300(data), b => Map.From(Serializer.Deserialize<L300Message>((ReadOnlySpan<byte>)b)), data);
    Row("Utf8Decimal", Map.ToUtf8(data), b => Map.From(Serializer.Deserialize<Utf8Message>((ReadOnlySpan<byte>)b)), data);
    Row("UnitsNanos", Map.ToUnitsNanos(data), b => Map.From(Serializer.Deserialize<UnitsNanosMessage>((ReadOnlySpan<byte>)b)), data);
    Row("FixedDecimal", Map.ToFixed(data), b => Map.From(Serializer.Deserialize<FixedDecimalMessage>((ReadOnlySpan<byte>)b)), data);
    Row("FixedDecimal packed", Map.ToFixedPacked(data), b => Map.From(Serializer.Deserialize<FixedDecimalPackedMessage>((ReadOnlySpan<byte>)b)), data);
    Row("Decimal64", Map.ToDecimal64(data), b => Map.From(Serializer.Deserialize<Decimal64Message>((ReadOnlySpan<byte>)b)), data);
    Row("Decimal64 packed", Map.ToDecimal64Packed(data), b => Map.From(Serializer.Deserialize<Decimal64PackedMessage>((ReadOnlySpan<byte>)b)), data);
    Row("PackedDecimal64", Map.ToPacked(data), b => Map.From(Serializer.Deserialize<PackedDecimal64Message>((ReadOnlySpan<byte>)b)), data);
    Row("Packed64 packed", Map.ToPackedPacked(data), b => Map.From(Serializer.Deserialize<PackedDecimal64PackedMessage>((ReadOnlySpan<byte>)b)), data);
    Row("Decimal128", Map.ToDecimal128(data), b => Map.From(Serializer.Deserialize<Decimal128Message>((ReadOnlySpan<byte>)b)), data);
    Row("Decimal128 packed", Map.ToDecimal128Packed(data), b => Map.From(Serializer.Deserialize<Decimal128PackedMessage>((ReadOnlySpan<byte>)b)), data);
    Console.WriteLine();

    Boundaries();
}

// Round-trips each edge value through a one-value message of each format (real protobuf serialization).
static void Boundaries()
{
    var inv = CultureInfo.InvariantCulture;
    decimal[] edges =
    [
        0m, 1.10m, 0.00000001m, 0.000000001m, 0.0000000001m, 0.0000000000000000000000000001m,
        0.3333333333333333333333333333m,
        9999999999999999m, 99999999999999999m, 288230376151711743m, 288230376151711744m,
        92233720368.54775807m, 92233720368.54775808m,
        9223372036854775807m, 9223372036854775807.999999999m, 100000000000000000000m,
        decimal.MaxValue, decimal.MinValue,
    ];

    (string Name, Func<decimal, decimal> RoundTrip)[] formats =
    [
        ("Native", d => Map.From(RoundTrip(Map.ToNative([d])))[0]),
        ("String", d => Map.From(RoundTrip(Map.ToStrings([d])))[0]),
        ("UnitsNanos", d => Map.From(RoundTrip(Map.ToUnitsNanos([d])))[0]),
        ("FixedDecimal", d => Map.From(RoundTrip(Map.ToFixed([d])))[0]),
        ("Decimal64", d => Map.From(RoundTrip(Map.ToDecimal64([d])))[0]),
        ("Packed64", d => Map.From(RoundTrip(Map.ToPacked([d])))[0]),
        ("Decimal128", d => Map.From(RoundTrip(Map.ToDecimal128([d])))[0]),
    ];

    Console.WriteLine("Boundaries - ok | ok,s=N (value equal, scale became N) | overflow | too precise:");
    Console.WriteLine($"  {"value",-34}" + string.Concat(formats.Select(f => $"{f.Name,-13}")));
    foreach (var d in edges)
    {
        Console.Write($"  {d.ToString(inv),-34}");
        foreach (var f in formats) Console.Write($"{Probe(f.RoundTrip, d),-13}");
        Console.WriteLine();
    }
}

static string Probe(Func<decimal, decimal> roundTrip, decimal d)
{
    try
    {
        var r = roundTrip(d);
        if (r != d) return "WRONG";
        return r.Scale == d.Scale ? "ok" : $"ok,s={r.Scale}";
    }
    catch (OverflowException) { return "overflow"; }
    catch (ArgumentException) { return "too precise"; }
}

static void Same(string what, byte[] a, byte[] b)
{
    if (!a.AsSpan().SequenceEqual(b)) throw new InvalidOperationException($"{what}: wire bytes differ");
}

static T RoundTrip<T>(T msg) => Serializer.Deserialize<T>((ReadOnlySpan<byte>)Bytes(msg));

static void Row<T>(string name, T msg, Func<byte[], decimal[]> back, decimal[] expected)
{
    var bytes = Bytes(msg);
    var result = back(bytes);
    var ok = result.SequenceEqual(expected);
    Console.WriteLine($"  {name,-20} {bytes.Length,8:N0}  {(ok ? "round-trip OK" : "ROUND-TRIP FAILED")}");
    if (!ok) throw new InvalidOperationException($"{name} did not round-trip");
}

static string Hex<T>(T msg) => Convert.ToHexString(Bytes(msg));

static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
