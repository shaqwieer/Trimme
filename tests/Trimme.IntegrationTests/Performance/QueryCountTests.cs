using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Discovery;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Performance;

/// <summary>
/// Phase 17 N+1 detection: the database commands each request runs, counted by an EF Core interceptor on the seeded demo
/// data. A list page of many rows must not need a command per extra row compared with a page of one. The paged endpoints
/// come from the API's own OpenAPI document, so a new list is covered automatically.
/// </summary>
public sealed class QueryCountTests(PostgresFixture postgres)
{
    /// <summary>Upper bound for one shop's availability (session check and settings included).</summary>
    private const int AvailabilityCommandLimit = 15;

    [Fact]
    public async Task ListEndpoints_RunTheSameNumberOfCommands_ForOneRowAndForFifty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await Harness.StartAsync(postgres, "perf_lists", ct);

        var paged = await PagedListEndpointsAsync(harness.Anonymous, ct);
        paged.Count.ShouldBeGreaterThan(15, "the OpenAPI document lists the paged endpoints");

        var exercised = new List<string>();
        var growing = new List<string>();
        foreach (var path in paged)
        {
            var session = harness.For(path);
            if (session is null)
            {
                continue;
            }

            var (one, _) = await harness.CountAsync(session, $"{path}?page=1&pageSize=1", ct);
            var (fifty, rows) = await harness.CountAsync(session, $"{path}?page=1&pageSize=50", ct);
            if (one is null || fifty is null || rows < 2)
            {
                continue; // forbidden for this role, or too few rows on the demo data to tell
            }

            exercised.Add($"{path}: {one} → {fifty} ({rows} rows)");

            // A query per row adds at least one command for every extra row. A fixed step (a check that runs only when
            // some row needs it, such as the schedule check once a page holds an active booking) is not an N+1.
            if (fifty - one >= rows - 1)
            {
                growing.Add($"{path}: {one} commands for 1 row, {fifty} for {rows} rows");
            }
        }

        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, exercised));
        exercised.Count.ShouldBeGreaterThan(10, string.Join(Environment.NewLine, exercised));
        growing.ShouldBeEmpty();
    }

    /// <summary>
    /// Each hot read path runs a fixed set of queries whatever the data (the list test proves no query per row). The
    /// budgets are the counts measured in Phase 17 plus a little headroom, so a new per-item query fails here.
    /// </summary>
    [Theory]
    [InlineData("anonymous", "/api/v1/public/shops/search?lat=24.7136&lng=46.6753", 20)]
    [InlineData("anonymous", "/api/v1/public/shops/barber-house", 12)]
    [InlineData("anonymous", "/api/v1/public/shops/barber-house/status", 19)]
    [InlineData("anonymous", "/api/v1/public/shops/barber-house/availability/dates?serviceId=0199a0de-5a10-7000-8000-000000000411", 16)]
    [InlineData("owner", "/api/v1/shop/dashboard/overview", 15)]
    [InlineData("owner", "/api/v1/shop/calendar?from=2026-10-04&to=2026-10-10", 15)]
    [InlineData("admin", "/api/v1/admin/dashboard/overview", 19)]
    public async Task HotReadPaths_StayWithinTheirCommandBudget(string role, string path, int budget)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await Harness.StartAsync(postgres, "perf_hot", ct);
        var session = role switch { "owner" => harness.Owner, "admin" => harness.Admin, _ => harness.Anonymous };

        var (commands, _) = await harness.CountAsync(session, path, ct);

        commands.ShouldNotBeNull($"{path} answers 200");
        TestContext.Current.SendDiagnosticMessage($"{path}: {commands} commands");
        commands.Value.ShouldBeLessThanOrEqualTo(budget, path);
    }

    [Fact]
    public async Task Availability_ForADay_DoesNotQueryPerSlotOrPerProfessional()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await Harness.StartAsync(postgres, "perf_slots", ct);
        var date = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const string Service = "0199a0de-5a10-7000-8000-000000000411";

        // "Any professional" checks every eligible barber; one barber checks one. The work grows, the commands do not.
        var (anyone, _) = await harness.CountAsync(harness.Anonymous, $"/api/v1/public/shops/barber-house/availability/slots?serviceId={Service}&date={date}", ct);
        var (omar, _) = await harness.CountAsync(harness.Anonymous, $"/api/v1/public/shops/barber-house/availability/slots?serviceId={Service}&professionalId=0199a0de-5a10-7000-8000-000000000201&date={date}", ct);

        anyone.ShouldNotBeNull();
        omar.ShouldNotBeNull();
        TestContext.Current.SendDiagnosticMessage($"slots: any professional {anyone}, one professional {omar}");
        anyone.Value.ShouldBeLessThanOrEqualTo(omar.Value + 1);
        anyone.Value.ShouldBeLessThanOrEqualTo(AvailabilityCommandLimit);
    }

    [Fact]
    public async Task BatchedProbes_ReadTheSameNumberOfCommands_ForOneShopOrMany()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await Harness.StartAsync(postgres, "perf_probe", ct);
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var probe = scope.ServiceProvider.GetRequiredService<ISlotProbe>();
        var shops = new[] { DemoData.BarberHouse.Id, DemoData.AlAsala.Id };
        using var _ = scope.ServiceProvider.GetRequiredService<IPublicDataScope>().BeginMany(shops);
        var today = probe.Today("Asia/Riyadh", DateTimeOffset.UtcNow);
        var requests = DemoData.Professionals
            .Select(p => new SlotProbeRequest(p.ShopId, "Asia/Riyadh", 30, [new ProfessionalId(p.Id)], today, today.AddDays(1), 1))
            .ToList();

        harness.ResetCount();
        var one = await probe.ProbeManyAsync(requests.Take(1).ToList(), ct);
        var forOne = harness.Commands;
        harness.ResetCount();
        var all = await probe.ProbeManyAsync(requests, ct);
        var forAll = harness.Commands;

        one.Count.ShouldBe(1);
        all.Count.ShouldBe(requests.Count);
        requests.Select(r => r.ShopId).Distinct().Count().ShouldBe(2);
        TestContext.Current.SendDiagnosticMessage($"probe: {forOne} commands for 1 request, {forAll} for {requests.Count} across 2 shops");
        forAll.ShouldBeGreaterThanOrEqualTo(6, "the batch read the schedules: hours, closures, working hours, breaks, time off, bookings");
        forAll.ShouldBeLessThanOrEqualTo(forOne, $"the batch reads each kind of schedule data once for all shops ({forOne} for one, {forAll} for all)");
    }

    private static async Task<List<string>> PagedListEndpointsAsync(ApiSession session, CancellationToken ct)
    {
        using var response = await session.GetAsync("/openapi/v1.json", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var paths = new List<string>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (path.Name.Contains('{', StringComparison.Ordinal) || !path.Value.TryGetProperty("get", out var get)
                || !get.TryGetProperty("parameters", out var parameters))
            {
                continue;
            }

            if (parameters.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "pageSize"))
            {
                paths.Add(path.Name);
            }
        }

        return paths;
    }

    /// <summary>A seeded API whose database commands are counted, with a session for each kind of caller.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly CommandCounter _counter;

        private Harness(TrimmeApiFactory factory, CommandCounter counter)
        {
            Factory = factory;
            _counter = counter;
        }

        public TrimmeApiFactory Factory { get; }

        public ApiSession Anonymous { get; private set; } = null!;

        public ApiSession Admin { get; private set; } = null!;

        public ApiSession Owner { get; private set; } = null!;

        public ApiSession Customer { get; private set; } = null!;

        public static async Task<Harness> StartAsync(PostgresFixture postgres, string prefix, CancellationToken ct)
        {
            var counter = new CommandCounter();
            var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync(prefix, ct))
            {
                ConfigureTestServices = services => services.ConfigureDbContext<TrimmeDbContext>(options => options.AddInterceptors(counter)),
            };
            await factory.MigrateAsync(ct);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                foreach (var seeder in scope.ServiceProvider.GetServices<IDevSeeder>().OrderBy(s => s.Order))
                {
                    await seeder.SeedAsync(scope.ServiceProvider, ct);
                }
            }

            var harness = new Harness(factory, counter)
            {
                Anonymous = ApiSession.Create(factory),
                Admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct),
                Owner = await IdentityTestData.SignInStaffAsync(factory, DemoData.BarberHouse.OwnerEmail, ct, DemoData.DefaultPassword),
                Customer = await IdentityTestData.SignInCustomerAsync(factory, DemoCustomers.Noura.Mobile, ct),
            };
            return harness;
        }

        public int Commands => _counter.Count;

        public void ResetCount() => _counter.Reset();

        /// <summary>The caller that can open <paramref name="path"/>, by its prefix.</summary>
        public ApiSession? For(string path) => path switch
        {
            _ when path.StartsWith("/api/v1/admin/", StringComparison.Ordinal) => Admin,
            _ when path.StartsWith("/api/v1/shop/", StringComparison.Ordinal) => Owner,
            _ when path.StartsWith("/api/v1/me/", StringComparison.Ordinal) => Customer,
            _ when path.StartsWith("/api/v1/public/", StringComparison.Ordinal) => Anonymous,
            _ => null,
        };

        /// <summary>The commands one request ran, and the rows of a paged answer; null commands when it is not a 200.</summary>
        public async Task<(int? Commands, int Rows)> CountAsync(ApiSession session, string path, CancellationToken ct)
        {
            _counter.Reset();
            using var response = await session.GetAsync(path, ct);
            var commands = _counter.Count;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (null, 0);
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var rows = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("items", out var items)
                ? items.GetArrayLength()
                : json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.GetArrayLength() : 0;
            return (commands, rows);
        }

        public async ValueTask DisposeAsync()
        {
            Anonymous.Dispose();
            Admin.Dispose();
            Owner.Dispose();
            Customer.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>Counts every database command EF Core runs (reads and writes).</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
