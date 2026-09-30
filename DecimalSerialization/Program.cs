using System.Buffers;
using System.Globalization;
using BenchmarkDotNet.Running;
using DecimalSerialization;
using ProtoBuf;

var inv = CultureInfo.InvariantCulture;

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
        FixedDecimal = Map.ToList<FixedDecimal>(values),
        Decimal64 = Map.ToList<Decimal64>(values),
        PackedDecimal64 = Map.ToList<PackedDecimal64>(values),
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
    Console.WriteLine($"Sample {sample.ToString(inv)} - one value as a repeated-field item:");
    Console.WriteLine($"  Native (bcl)      {Hex(Map.ToNative([sample]))}");
    Console.WriteLine($"  String            {Hex(Map.ToStrings([sample]))}");
    Console.WriteLine($"  Level300          {Hex(Map.ToL300([sample]))}");
    Console.WriteLine($"  UnitsNanos        {Hex(Map.ToUnitsNanos([sample]))}");
    Console.WriteLine($"  FixedDecimal      {Hex(Map.ToFixed([sample]))}");
    Console.WriteLine($"  Decimal64 (BID)   {Hex(Map.ToDecimal64([sample]))}");
    Console.WriteLine($"  PackedDecimal64   {Hex(Map.ToPacked([sample]))}");
    Console.WriteLine($"  -123.45 bcl       {Hex(Map.ToNative([-sample]))}");
    Console.WriteLine();

    var data = Data.Make(1000);
    Console.WriteLine("Benchmark data (1,000 values), wire bytes:");
    Row("Native (bcl)", Map.ToNative(data), b => Map.From(Serializer.Deserialize<NativeMessage>((ReadOnlySpan<byte>)b)), data);
    Row("String", Map.ToStrings(data), b => Map.From(Serializer.Deserialize<StringMessage>((ReadOnlySpan<byte>)b)), data);
    Row("Level300", Map.ToL300(data), b => Map.From(Serializer.Deserialize<L300Message>((ReadOnlySpan<byte>)b)), data);
    Row("UnitsNanos", Map.ToUnitsNanos(data), b => Map.From(Serializer.Deserialize<UnitsNanosMessage>((ReadOnlySpan<byte>)b)), data);
    Row("FixedDecimal", Map.ToFixed(data), b => Map.From(Serializer.Deserialize<FixedDecimalMessage>((ReadOnlySpan<byte>)b)), data);
    Row("Decimal64 (BID)", Map.ToDecimal64(data), b => Map.From(Serializer.Deserialize<Decimal64Message>((ReadOnlySpan<byte>)b)), data);
    Row("PackedDecimal64", Map.ToPacked(data), b => Map.From(Serializer.Deserialize<PackedDecimal64Message>((ReadOnlySpan<byte>)b)), data);
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

static T RoundTrip<T>(T msg) => Serializer.Deserialize<T>((ReadOnlySpan<byte>)Bytes(msg));

static void Row<T>(string name, T msg, Func<byte[], decimal[]> back, decimal[] expected)
{
    var bytes = Bytes(msg);
    var result = back(bytes);
    var ok = result.SequenceEqual(expected);
    Console.WriteLine($"  {name,-18} {bytes.Length,8:N0}  {(ok ? "round-trip OK" : "ROUND-TRIP FAILED")}");
    if (!ok) throw new InvalidOperationException($"{name} did not round-trip");
}

static string Hex<T>(T msg) => Convert.ToHexString(Bytes(msg));

static byte[] Bytes<T>(T msg) { var w = new ArrayBufferWriter<byte>(); Serializer.Serialize(w, msg); return w.WrittenSpan.ToArray(); }
