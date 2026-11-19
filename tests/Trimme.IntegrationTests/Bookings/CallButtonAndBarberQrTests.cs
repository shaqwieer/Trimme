using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.IntegrationTests.Infrastructure;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Bookings;

/// <summary>
/// D-155: the public page's «اتصال» button is the admin's only (off by default, never on a shop-facing payload, never its
/// number in the audit trail), and every barber has their own QR code, made when they are added or turned on again, and by
/// the <c>migrate</c> backfill for those added before.
/// </summary>
public sealed class CallButtonAndBarberQrTests(PostgresFixture postgres)
{
    private static async Task<object?> ScalarAsync(TrimmeApiFactory factory, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    [Fact]
    public async Task TheCallButton_IsTheAdminsOnly_OffByDefault_AndShownOnThePublicPageOnlyWhenOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "call_button", ct);
        using var anonymous = ApiSession.Create(w.Factory);
        var shop = $"/api/v1/admin/shops/{w.Shops.A.ShopId}";

        // Off by default: nothing on the public page.
        var detail = await OkAsync(w.Admin.GetAsync(shop, ct), ct);
        detail.GetProperty("callButtonEnabled").GetBoolean().ShouldBeFalse();
        detail.GetProperty("callButtonPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("callPhone").ValueKind.ShouldBe(JsonValueKind.Null);

        // On needs a number; an invalid one is refused; the owner cannot set it.
        await FailsAsync(w.Admin.PutAsync($"{shop}/call-button", new { enabled = true }, ct), HttpStatusCode.BadRequest, "validation.required", ct);
        await FailsAsync(w.Admin.PutAsync($"{shop}/call-button", new { enabled = true, phone = "12" }, ct), HttpStatusCode.BadRequest, "validation.phone_invalid", ct);
        (await w.OwnerA.PutAsync($"{shop}/call-button", new { enabled = true, phone = "0551112233" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var on = await OkAsync(w.Admin.PutAsync($"{shop}/call-button", new { enabled = true, phone = "0551112233" }, ct), ct);
        on.GetProperty("callButtonEnabled").GetBoolean().ShouldBeTrue();
        on.GetProperty("callButtonPhone").GetString().ShouldBe("+966551112233");
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("callPhone").GetString().ShouldBe("+966551112233");

        // The owner's own pages never carry it.
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/profile", ct), ct)).GetRawText().ShouldNotContain("551112233");

        // Off keeps the number for next time; the public page shows nothing; on again needs no number.
        await OkAsync(w.Admin.PutAsync($"{shop}/call-button", new { enabled = false }, ct), ct);
        (await OkAsync(anonymous.GetAsync($"/api/v1/public/shops/{w.SlugA}", ct), ct)).GetProperty("callPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OkAsync(w.Admin.PutAsync($"{shop}/call-button", new { enabled = true }, ct), ct)).GetProperty("callButtonPhone").GetString().ShouldBe("+966551112233");

        // Audited with a code and its arguments, never the number.
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE action = 'shop.call_button_set' AND summary_code = action", ct)).ShouldBe(3L);
        (await ScalarAsync(w.Factory, "SELECT count(*) FROM administration.audit_entries WHERE summary_args::text LIKE '%551112233%' OR summary LIKE '%551112233%'", ct))
            .ShouldBe(0L);
    }

    [Fact]
    public async Task EveryBarber_GetsTheirOwnQrCode_WhenAdded_TurnedOnAgain_OrByTheBackfill()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "barber_qr", ct);
        static string Codes(Guid professionalId) => $"SELECT count(*) FROM qr.qr_code_links WHERE professional_id = '{professionalId}'";

        // The world's barbers were added by the admin: one code each.
        (await ScalarAsync(w.Factory, Codes(w.Faisal), ct)).ShouldBe(1L);
        (await ScalarAsync(w.Factory, Codes(w.Omar), ct)).ShouldBe(1L);

        // The owner adds a barber: a code at once, and the shop sees it.
        var added = await OkAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", new
        {
            nameAr = "سالم",
            nameEn = "Salem",
            whatsAppNumber = "0551237788",
            notificationsEnabled = true,
        }, ct), ct, HttpStatusCode.Created);
        var salem = added.GetProperty("barber").GetProperty("id").GetGuid();
        (await ScalarAsync(w.Factory, Codes(salem), ct)).ShouldBe(1L);
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/qr/codes", ct), ct)).GetProperty("items").EnumerateArray()
            .Count(c => c.GetProperty("targetType").GetString() == "Professional").ShouldBe(3);

        // A barber added before D-155 without a code: the backfill gives one, once.
        await using (var connection = new NpgsqlConnection(w.Factory.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var clear = new NpgsqlCommand(
                $"DELETE FROM qr.qr_code_routes WHERE link_id IN (SELECT id FROM qr.qr_code_links WHERE professional_id = '{w.Omar}'); " +
                $"DELETE FROM qr.qr_code_links WHERE professional_id = '{w.Omar}'", connection);
            await clear.ExecuteNonQueryAsync(ct);
        }

        for (var run = 0; run < 2; run++)
        {
            await using var scope = w.Factory.Services.CreateAsyncScope();
            var backfill = scope.ServiceProvider.GetServices<IReferenceDataSynchronizer>().Single(s => s.Name == "Barber QR codes");
            await backfill.SynchronizeAsync(scope.ServiceProvider, ct);
        }

        (await ScalarAsync(w.Factory, Codes(w.Omar), ct)).ShouldBe(1L);
        (await ScalarAsync(w.Factory, Codes(w.Faisal), ct)).ShouldBe(1L);
    }
}
