// rcops — small CLI wrapper around the live aggregator so an operations
// script can produce a Month-End Close snapshot against any database
// (production, restored backup, sandbox) without booting the web app.
//
// Used today by `scripts/restore-drill.ps1` to prove a restored backup
// rehydrates to the same close-state as the snapshot that was stored at
// close time. If the two snapshots diverge, either the backup is corrupt
// or the aggregator has changed shape since the close — both are things
// the drill is there to catch.
//
// Commands:
//   rcops mec-snapshot --connection "<cs>" --period yyyy-MM
//       Run the aggregator against the connection string and print the
//       resulting payload as JSON to stdout. Exit 0 on success, 1 on
//       error (with the message on stderr).
//
//   rcops compare-snapshot --connection "<cs>" --period yyyy-MM
//       Run mec-snapshot, then read the stored ClosedPeriod.SnapshotJson
//       for the same period, normalise (drop volatile keys like
//       periodStart/periodEnd which encode local-time formatting), and
//       diff the structure. Exit 0 if equivalent, 2 if divergent.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

return args[0] switch
{
    "mec-snapshot"      => await McMain.SnapshotAsync(args.Skip(1).ToArray()),
    "compare-snapshot"  => await McMain.CompareAsync(args.Skip(1).ToArray()),
    "--help" or "-h"    => PrintUsage(),
    _ => Fail($"Unknown command: {args[0]}"),
};

static int PrintUsage()
{
    Console.WriteLine("rcops — RentCollection operations CLI");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  mec-snapshot     --connection <cs> --period yyyy-MM");
    Console.WriteLine("  compare-snapshot --connection <cs> --period yyyy-MM");
    return 0;
}

static int Fail(string msg) { Console.Error.WriteLine($"ERROR: {msg}"); return 1; }

internal static class McMain
{
    /// <summary>Parse a flat <c>--name value</c> argv into a dictionary.</summary>
    private static Dictionary<string, string> ParseArgs(string[] argv)
    {
        var result = new Dictionary<string, string>();
        for (int i = 0; i + 1 < argv.Length; i += 2)
        {
            var key = argv[i].TrimStart('-');
            result[key] = argv[i + 1];
        }
        return result;
    }

    private static (string conn, string period, IMonthEndCloseService svc, ApplicationDbContext ctx)
        WireUp(string[] argv)
    {
        var p = ParseArgs(argv);
        if (!p.TryGetValue("connection", out var conn) || string.IsNullOrWhiteSpace(conn))
            throw new ArgumentException("--connection is required");
        if (!p.TryGetValue("period", out var period)
            || !System.Text.RegularExpressions.Regex.IsMatch(period, @"^\d{4}-\d{2}$"))
            throw new ArgumentException("--period must be yyyy-MM");

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(conn)
            .Options;
        var ctx = new ApplicationDbContext(opts);
        var svc = new MonthEndCloseService(ctx, new SystemClock());
        return (conn, period, svc, ctx);
    }

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> SnapshotAsync(string[] argv)
    {
        try
        {
            var (_, period, svc, ctx) = WireUp(argv);
            await using (ctx)
            {
                var state = await svc.BuildCloseStateAsync(period);
                Console.WriteLine(JsonSerializer.Serialize(state, Pretty));
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> CompareAsync(string[] argv)
    {
        try
        {
            var (_, period, svc, ctx) = WireUp(argv);
            await using (ctx)
            {
                var stored = await ctx.ClosedPeriods
                    .Where(c => c.Period == period)
                    .Select(c => c.SnapshotJson)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrEmpty(stored))
                {
                    Console.Error.WriteLine($"ERROR: no ClosedPeriod row for {period} (drill needs a closed period)");
                    return 1;
                }

                var liveRaw = await svc.BuildCloseStateAsync(period);
                var live = JsonSerializer.SerializeToNode(liveRaw,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
                var snap = JsonNode.Parse(stored)!;

                Normalise(live);
                Normalise(snap);

                if (JsonNode.DeepEquals(live, snap))
                {
                    Console.WriteLine($"OK: restored snapshot for {period} matches the live aggregator.");
                    return 0;
                }

                Console.Error.WriteLine($"DIVERGENT: snapshot for {period} does not match the live aggregator.");
                Console.Error.WriteLine("--- live ---");
                Console.Error.WriteLine(live.ToJsonString(Pretty));
                Console.Error.WriteLine("--- stored ---");
                Console.Error.WriteLine(snap.ToJsonString(Pretty));
                return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Strip keys that are stable in business meaning but volatile in
    /// representation (DateTimes get serialized with different millisecond
    /// precision across runs; lastSentAt etc reflect *when* the dashboard
    /// email was sent, which differs between when the period was closed
    /// and when the drill replays). Keep the colour-bearing fields —
    /// those are what the audit needs to be identical.
    /// </summary>
    private static void Normalise(JsonNode root)
    {
        if (root is not JsonObject obj) return;
        obj.Remove("periodStart");
        obj.Remove("periodEnd");
        if (obj["closed"] is JsonObject closed)
        {
            closed.Remove("closedAt");
            closed.Remove("ClosedAt");
        }
        if (obj["steps"] is JsonObject steps)
        {
            foreach (var (_, stepNode) in steps)
            {
                if (stepNode is JsonObject step)
                {
                    step.Remove("lastSentAt");
                    step.Remove("lastBatchDate");
                }
            }
        }
    }
}
