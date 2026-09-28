using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Bookings.Domain;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// R-BKG-03/04/05 (spec §11, §19): under concurrency exactly one booking wins a contested time, for create, partial
/// overlaps and reschedule; the same idempotency key in parallel produces one booking; and the database itself refuses an
/// overlap (exclusion constraint), whatever the application does.
/// </summary>
public sealed class BookingConcurrencyTests(PostgresFixture postgres)
{
    private const int Contenders = 8;

    private static DateOnly Target => TodayAt(DateTimeOffset.UtcNow).AddDays(2);

    private static async Task<(int Created, int Conflicts, List<string> Other)> RaceAsync(IEnumerable<Func<Task<HttpResponseMessage>>> requests, CancellationToken ct)
    {
        var responses = await Task.WhenAll(requests.Select(r => Task.Run(r, ct)));
        var created = 0;
        var conflicts = 0;
        var other = new List<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                created++;
            }
            else if (response.StatusCode == HttpStatusCode.Conflict && body.Contains("booking.slot_unavailable", StringComparison.Ordinal))
            {
                conflicts++;
            }
            else
            {
                other.Add($"{(int)response.StatusCode} {body}");
            }

            response.Dispose();
        }

        return (created, conflicts, other);
    }

    private static async Task<int> ActiveBookingsAsync(TrimmeApiFactory factory, Guid professionalId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var id = new ProfessionalId(professionalId);
        var active = BookingRules.Active.ToArray();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<Booking>()
            .CountAsync(b => b.ProfessionalId == id && active.Contains(b.Status), ct);
    }

    private static async Task<int> OutboxCountAsync(TrimmeApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<OutboxMessage>().CountAsync(ct);
    }

    [Fact]
    public async Task ConcurrentBookings_ForTheSameProfessionalAndTime_ExactlyOneSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_race", ct);
        var customers = new List<ApiSession>();
        for (var i = 0; i < Contenders; i++)
        {
            customers.Add(await CustomerAsync(w.Factory, $"عميل {i}", ct));
        }

        try
        {
            var slot = At(Target, 10);
            var race = await RaceAsync(customers.Select(c => (Func<Task<HttpResponseMessage>>)(() => BookAsync(c, w.SlugA, w.Haircut, w.Faisal, slot, ct))), ct);
            race.Other.ShouldBeEmpty();
            race.Created.ShouldBe(1);
            race.Conflicts.ShouldBe(Contenders - 1);
            (await ActiveBookingsAsync(w.Factory, w.Faisal, ct)).ShouldBe(1);
            (await OutboxCountAsync(w.Factory, ct)).ShouldBe(1, "losers roll back their outbox rows (R-BKG-08)");

            // Partial overlaps: a 60-minute package at 11:00 against 30-minute haircuts at 11:15 and 11:30.
            var partial = new List<Func<Task<HttpResponseMessage>>>
            {
                () => BookAsync(customers[0], w.SlugA, w.Haircut, w.Omar, At(Target, 11), ct, packageId: w.Package),
                () => BookAsync(customers[1], w.SlugA, w.Haircut, w.Omar, At(Target, 11, 15), ct),
                () => BookAsync(customers[2], w.SlugA, w.Haircut, w.Omar, At(Target, 11, 30), ct),
            };
            var overlap = await RaceAsync(partial, ct);
            overlap.Other.ShouldBeEmpty();
            overlap.Created.ShouldBeInRange(1, 2, "11:15 and 11:30 overlap each other and the package; only the package or a non-overlapping pair can win");
            (await ActiveBookingsAsync(w.Factory, w.Omar, ct)).ShouldBe(overlap.Created);

            // Whoever won, no two active bookings of Omar overlap.
            await using var scope = w.Factory.Services.CreateAsyncScope();
            using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
            var omar = new ProfessionalId(w.Omar);
            var rows = await scope.ServiceProvider.GetRequiredService<TrimmeDbContext>().Set<Booking>().AsNoTracking()
                .Where(b => b.ProfessionalId == omar).OrderBy(b => b.StartsAt).ToListAsync(ct);
            rows.Zip(rows.Skip(1)).ShouldAllBe(pair => pair.First.EndsAt <= pair.Second.StartsAt);
        }
        finally
        {
            customers.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task ConcurrentReschedules_IntoTheSameTime_ExactlyOneSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_race_move", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        using var khalid = await CustomerAsync(w.Factory, "خالد", ct);
        var a = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 9), ct), ct, HttpStatusCode.Created);
        var b = await OkAsync(BookAsync(khalid, w.SlugA, w.Haircut, w.Faisal, At(Target, 15), ct), ct, HttpStatusCode.Created);
        var target = At(Target, 12);

        var race = await RaceAsync(
        [
            () => noura.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{a.GetProperty("id").GetGuid()}/reschedule", new { startsAt = target, version = a.GetProperty("version").GetUInt32() }, ct, headers: Key()),
            () => khalid.SendAsync(HttpMethod.Post, $"/api/v1/me/bookings/{b.GetProperty("id").GetGuid()}/reschedule", new { startsAt = target, version = b.GetProperty("version").GetUInt32() }, ct, headers: Key()),
        ], ct);
        race.Other.ShouldBeEmpty();
        race.Created.ShouldBe(1);
        race.Conflicts.ShouldBe(1);
        (await ActiveBookingsAsync(w.Factory, w.Faisal, ct)).ShouldBe(2, "the loser keeps its original time");
    }

    [Fact]
    public async Task TheSameIdempotencyKey_InParallel_CreatesOneBooking_AndBothSeeIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_race_key", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var slot = At(Target, 14);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => BookAsync(noura, w.SlugA, w.Haircut, null, slot, ct, key: "double-tap"), ct)));
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
            ids.Add((await response.JsonAsync(ct)).GetProperty("id").GetGuid());
            response.Dispose();
        }

        ids.Distinct().ShouldHaveSingleItem();
        (await ActiveBookingsAsync(w.Factory, w.Faisal, ct) + await ActiveBookingsAsync(w.Factory, w.Omar, ct)).ShouldBe(1);
        (await OutboxCountAsync(w.Factory, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task TheDatabase_RefusesOverlappingActiveBookings_EvenWithoutTheApplication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "bkg_exclusion", ct);
        using var noura = await CustomerAsync(w.Factory, "نورة", ct);
        var booked = await OkAsync(BookAsync(noura, w.SlugA, w.Haircut, w.Faisal, At(Target, 16), ct), ct, HttpStatusCode.Created);

        await using var scope = w.Factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var shopId = new ShopId(w.Shops.A.ShopId);
        var faisal = new BookedProfessional(new ProfessionalId(w.Faisal), "فيصل", "Faisal");
        var item = new BookedItem(w.Haircut, null, "حلاقة", null, 60m, "SAR", 30, []);
        Booking Overlapping(params BookingStatus[] path) =>
            Booking.Seeded(new BookingId(Guid.CreateVersion7()), shopId, null, "raw", faisal, item, At(Target, 16, 10), BookingChannel.WalkIn, path, "raw", DateTimeOffset.UtcNow);

        db.Add(Overlapping(BookingStatus.Confirmed));
        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        DatabaseErrors.IsExclusionViolation(refused).ShouldBeTrue();
        db.ChangeTracker.Clear();

        // Inactive statuses do not hold the time.
        db.Add(Overlapping(BookingStatus.Confirmed, BookingStatus.CancelledByShop));
        db.Add(Overlapping(BookingStatus.Confirmed, BookingStatus.NoShow));
        await db.SaveChangesAsync(ct);

        // Cancelling the original frees the time for a new active booking.
        var original = await db.Set<Booking>().SingleAsync(b => b.Id == new BookingId(booked.GetProperty("id").GetGuid()), ct);
        original.ApplyShopTransition(BookingStatus.CancelledByShop, BookingActor.System, "freed", DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.Add(Overlapping(BookingStatus.Confirmed));
        await db.SaveChangesAsync(ct);

        // The same-shop key: a booking cannot point at another shop's service.
        db.ChangeTracker.Clear();
        db.Add(Booking.Seeded(
            new BookingId(Guid.CreateVersion7()), shopId, null, "raw", faisal, item with { ServiceId = w.ServiceB }, At(Target, 18), BookingChannel.WalkIn,
            [BookingStatus.Confirmed], null, DateTimeOffset.UtcNow));
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct))).InnerException!.Message.ShouldContain("fk_bookings_shop_services_shop_id_service_id");
    }
}
