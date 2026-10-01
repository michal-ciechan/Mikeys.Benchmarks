using System.Diagnostics;
using BenchmarkDotNet.Running;
using HttpClientMocking;
using Microsoft.Extensions.DependencyInjection;

// --check            every fake must answer all endpoints correctly and refuse unmatched / wrong-body requests
// --cold <fake>      one cold-process run: time from a fresh process to 20 working endpoints (one line)
// --cold-all [runs]  spawn --cold for every fake, print medians
// anything else      BenchmarkDotNet (see README for filters)
if (args.Contains("--check")) { await Check(); return; }
if (args is ["--cold", var coldName]) { await Cold(coldName); return; }
if (args is ["--cold-all", ..]) { await ColdAll(args.Length > 1 ? int.Parse(args[1]) : 15); return; }

await Check();
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

static ServiceProvider Build(IFake fake)
{
    var services = new ServiceCollection();
    fake.Configure(services);
    return services.BuildServiceProvider();
}

static async Task Check()
{
    foreach (var name in Fakes.Names)
        foreach (var n in new[] { 20, 200 })
        {
            using var fake = Fakes.Create(name);
            fake.Start();
            var endpoints = Scenario.Build(n);
            fake.Register(endpoints);
            await using var sp = Build(fake);
            var client = sp.GetRequiredService<ApiClient>();

            foreach (var e in endpoints)
            {
                var r = await client.CallAsync(e);
                if (r.Id != e.ExpectedId) throw new Exception($"{name}/{n}: {e.Method} {e.PathAndQuery} returned id {r.Id}, expected {e.ExpectedId}");
            }

            // must not match: unknown path, a POST whose body differs from the stubbed one, an extra query parameter
            var post = endpoints.First(e => e.IsPost);
            var failures = 0;
            var accepted = new List<string>();
            try { await client.GetAsync("/does/not/exist"); accepted.Add("unknown path"); } catch { failures++; }
            try { await client.PostAsync(post.Path, post.Request! with { Name = "different" }); accepted.Add("different POST body"); } catch { failures++; }
            try { await client.GetAsync(endpoints[1].PathAndQuery + "&extra=1"); accepted.Add("extra query parameter"); } catch { failures++; }
            if (failures != 3) Console.WriteLine($"NOTE {name}/{n}: wrongly accepted: {string.Join(", ", accepted)}");

            Console.WriteLine($"check ok: {name,-22} {n,4} endpoints, {failures}/3 refusals");
        }
}

// One cold run: how long a fresh process takes to go from nothing to 20 stubbed endpoints answering.
static async Task Cold(string name)
{
    var endpoints = Scenario.Build(20);
    var sw = Stopwatch.StartNew();
    double Lap() { var t = sw.Elapsed.TotalMilliseconds; sw.Restart(); return t; }

    var fake = Fakes.Create(name);                          // loads the library
    var create = Lap();
    fake.Start();
    var start = Lap();
    fake.Register(endpoints);
    var register = Lap();
    await using var sp = Build(fake);
    var client = sp.GetRequiredService<ApiClient>();
    var provider = Lap();
    await client.CallAsync(endpoints[0]);
    var first = Lap();
    foreach (var e in endpoints.Skip(1)) await client.CallAsync(e);
    var rest = Lap();
    fake.Dispose();
    Console.WriteLine($"COLD {name} {create:F1} {start:F1} {register:F1} {provider:F1} {first:F1} {rest:F1}");
}

static async Task ColdAll(int runs)
{
    var dll = typeof(Program).Assembly.Location;
    Console.WriteLine($"{"fake",-22} {"create",7} {"start",7} {"register",8} {"provider",8} {"first",7} {"other19",8} {"TOTAL",8}   (median ms of {runs} fresh processes; min..max total)");
    foreach (var name in Fakes.Names)
    {
        var rows = new List<double[]>();
        for (var i = 0; i < runs + 1; i++)                  // first run discarded (OS file cache)
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, $"\"{dll}\" --cold {name}") { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var line = output.Split('\n').FirstOrDefault(l => l.StartsWith("COLD ")) ?? throw new Exception($"{name} failed: {output}");
            if (i > 0) rows.Add(line.Split(' ').Skip(2).Select(double.Parse).ToArray());
        }
        double Med(int col) { var s = rows.Select(r => r[col]).Order().ToArray(); return s[s.Length / 2]; }
        var totals = rows.Select(r => r.Sum()).Order().ToArray();
        Console.WriteLine($"{name,-22} {Med(0),7:F0} {Med(1),7:F0} {Med(2),8:F0} {Med(3),8:F0} {Med(4),7:F0} {Med(5),8:F0} {totals[totals.Length / 2],8:F0}   {totals[0]:F0}..{totals[^1]:F0}");
    }
}
