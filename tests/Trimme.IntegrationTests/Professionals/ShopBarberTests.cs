using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Administration.Domain;
using Trimme.Tests.Shared;
using static Trimme.IntegrationTests.Bookings.BookingTestData;

namespace Trimme.IntegrationTests.Professionals;

/// <summary>
/// D-146: the shop owner adds and edits their own barbers, and a new barber gets a login at once, with a temporary
/// password the server generates and answers once. The owner sees the number masked only; another shop's owner finds
/// nothing; a number another barber has (in any shop) is refused without saying where; a barber cannot manage barbers.
/// </summary>
public sealed class ShopBarberTests(PostgresFixture postgres)
{
    private static object NewBarber(string number, string nameEn = "Saad") => new
    {
        nameAr = "سعد",
        nameEn,
        specialtyAr = "قص وتدريج",
        specialtyEn = (string?)null,
        bioAr = (string?)null,
        bioEn = (string?)null,
        whatsAppNumber = number,
        notificationsEnabled = true,
    };

    private static async Task<(Guid Id, string Password, string Body)> AddAsync(ApiSession owner, string number, CancellationToken ct, string nameEn = "Saad")
    {
        using var response = await owner.PostAsync("/api/v1/shop/professionals", NewBarber(number, nameEn), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        var json = JsonDocument.Parse(body).RootElement;
        return (json.GetProperty("barber").GetProperty("id").GetGuid(), json.GetProperty("temporaryPassword").GetString()!, body);
    }

    private static async Task<List<AuditEntry>> AuditAsync(TrimmeApiFactory factory, Guid entityId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        return await db.Set<AuditEntry>().AsNoTracking().Where(e => e.EntityId == entityId.ToString()).OrderBy(e => e.Sequence).ToListAsync(ct);
    }

    [Fact]
    public async Task TheOwner_AddsABarber_WhoGetsALoginAtOnce_WithAGeneratedPassword_ReplacedAtTheFirstSignIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "shop_barber_add", ct);

        // The owner may manage barbers but no longer books walk-ins (D-146).
        var permissions = (await OkAsync(w.OwnerA.GetAsync("/api/v1/me", ct), ct)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        permissions.ShouldContain("Shop.Barbers.Manage");
        permissions.ShouldContain("Shop.Barbers.ManageLogin");
        permissions.ShouldNotContain("Shop.Bookings.CreateWalkIn");

        var (id, password, body) = await AddAsync(w.OwnerA, "0512341001", ct);
        body.ShouldNotContain("512341001", customMessage: "the number is answered masked only");
        var barber = JsonDocument.Parse(body).RootElement.GetProperty("barber");
        barber.GetProperty("nameAr").GetString().ShouldBe("سعد");
        barber.GetProperty("status").GetString().ShouldBe("Active");
        barber.GetProperty("slug").GetString().ShouldBe("saad");
        barber.GetProperty("whatsApp").GetProperty("masked").GetString().ShouldNotBeNullOrEmpty();
        barber.GetProperty("whatsApp").GetProperty("notificationsEnabled").GetBoolean().ShouldBeTrue();
        password.Length.ShouldBe(12);

        // In the shop's own list, in the right shop, and the login is on, waiting for its first sign-in.
        (await OkAsync(w.OwnerA.GetAsync("/api/v1/shop/professionals", ct), ct)).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldContain(id);
        var login = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{id}/login", ct), ct);
        login.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        login.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        login.GetProperty("hasNumber").GetBoolean().ShouldBeTrue();
        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{id}", ct), ct);
        detail.ToString().ShouldNotContain("512341001");

        // The barber signs in with the number and the password the owner handed over, and must replace it.
        using var saad = ApiSession.Create(w.Factory);
        await FailsAsync(BarberLoginTests.SignInAsync(saad, "0512341001", password, ct), HttpStatusCode.Forbidden, "auth.password_change_required", ct);
        var me = (await OkAsync(saad.PostAsync("/api/v1/auth/password/change-temporary",
            new { phone = "0512341001", password, newPassword = BarberLoginTests.Chosen }, ct), ct)).GetProperty("user");
        me.GetProperty("professionalId").GetGuid().ShouldBe(id);
        me.GetProperty("shopId").GetGuid().ShouldBe(w.Shops.A.ShopId);
        using (var again = ApiSession.Create(w.Factory))
        {
            await FailsAsync(BarberLoginTests.SignInAsync(again, "0512341001", password, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
        }

        // A barber manages no barbers, theirs or a colleague's.
        await FailsAsync(saad.PostAsync("/api/v1/shop/professionals", NewBarber("0512341009"), ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(saad.GetAsync($"/api/v1/shop/professionals/{id}", ct), HttpStatusCode.Forbidden, null, ct);
        await FailsAsync(saad.GetAsync($"/api/v1/shop/professionals/{w.Omar}/services", ct), HttpStatusCode.Forbidden, null, ct);

        // The audit trail says what happened, never the number or the password.
        var audit = await AuditAsync(w.Factory, id, ct);
        audit.Select(e => e.Action).ShouldBe(["professional.created", "barber_login.enabled"], ignoreOrder: true);
        audit.ShouldAllBe(e => e.ShopId == w.Shops.A.ShopId);
        string.Join('|', audit.Select(e => $"{e.Summary} {e.Reason}")).ShouldNotContain(password);
        string.Join('|', audit.Select(e => $"{e.Summary} {e.Reason}")).ShouldNotContain("512341001");
    }

    [Fact]
    public async Task ANumberAnotherBarberHas_IsRefused_InAnyShop_AndNothingIsLeftBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "shop_barber_taken", ct);
        await AddAsync(w.OwnerA, "0512342001", ct);

        // Same shop, then another shop: shop B's owner cannot see shop A's barbers, the database's unique indexes can.
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", NewBarber("+966512342001", "Other"), ct), HttpStatusCode.BadRequest, "validation.whatsapp_taken", ct);
        var before = (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/professionals", ct), ct)).GetArrayLength();
        var refused = await FailsAsync(w.OwnerB.PostAsync("/api/v1/shop/professionals", NewBarber("0512342001", "Other"), ct), HttpStatusCode.BadRequest, "validation.whatsapp_taken", ct);
        refused.ShouldNotContain(w.Shops.A.ShopId.ToString());
        (await OkAsync(w.OwnerB.GetAsync("/api/v1/shop/professionals", ct), ct)).GetArrayLength().ShouldBe(before, "the refused barber was not saved");

        // Shop B's barber with a free number works; changing it to shop A's barber's number is refused the same way.
        var (proB, _, _) = await AddAsync(w.OwnerB, "0512342002", ct);
        await FailsAsync(w.OwnerB.PutAsync($"/api/v1/shop/professionals/{proB}/whatsapp", new { whatsAppNumber = "0512342001", notificationsEnabled = true }, ct),
            HttpStatusCode.BadRequest, "validation.whatsapp_taken", ct);

        // The number is required and must be a mobile; the names are required.
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", NewBarber(""), ct), HttpStatusCode.BadRequest, "validation.required", ct);
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", NewBarber("0112345678"), ct), HttpStatusCode.BadRequest, "validation.phone_invalid", ct);
        await FailsAsync(w.OwnerA.PostAsync("/api/v1/shop/professionals", NewBarber("0512342003", nameEn: " "), ct), HttpStatusCode.BadRequest, "validation.required", ct);
    }

    [Fact]
    public async Task TheOwner_EditsTheirBarber_Profile_Number_Services_Photo_AndStatus_AnotherShopFindsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var w = await ArrangeAsync(postgres, "shop_barber_edit", ct);
        var (id, password, _) = await AddAsync(w.OwnerA, "0512343001", ct);
        using (var first = ApiSession.Create(w.Factory))
        {
            await OkAsync(first.PostAsync("/api/v1/auth/password/change-temporary", new { phone = "0512343001", password, newPassword = BarberLoginTests.Chosen }, ct), ct);
        }

        // Another shop's owner can neither read nor change this barber (404, like an unknown id).
        string[] paths = [$"/api/v1/shop/professionals/{id}", $"/api/v1/shop/professionals/{id}/services", $"/api/v1/shop/professionals/{id}/login"];
        foreach (var path in paths)
        {
            await FailsAsync(w.OwnerB.GetAsync(path, ct), HttpStatusCode.NotFound, "professional.not_found", ct);
        }

        await FailsAsync(w.OwnerB.PutAsync($"/api/v1/shop/professionals/{id}", new { nameAr = "x", nameEn = "x", version = 0u }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.PutAsync($"/api/v1/shop/professionals/{id}/whatsapp", new { whatsAppNumber = "0512343999", notificationsEnabled = false }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.PutAsync($"/api/v1/shop/professionals/{id}/services", new { serviceIds = new[] { w.ServiceB } }, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.PostAsync($"/api/v1/shop/professionals/{id}/disable", null, ct), HttpStatusCode.NotFound, null, ct);
        await FailsAsync(w.OwnerB.DeleteAsync($"/api/v1/shop/professionals/{id}/avatar", ct), HttpStatusCode.NotFound, null, ct);

        // Profile, with optimistic concurrency; the page address stays.
        var detail = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{id}", ct), ct);
        var version = detail.GetProperty("version").GetUInt32();
        var edited = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}",
            new { nameAr = "سعد الحربي", nameEn = "Saad Alharbi", specialtyAr = "لحية", specialtyEn = "Beard", bioAr = "عشر سنوات", bioEn = (string?)null, version }, ct), ct);
        edited.GetProperty("nameAr").GetString().ShouldBe("سعد الحربي");
        edited.GetProperty("slug").GetString().ShouldBe("saad");
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}", new { nameAr = "قديم", nameEn = "Stale", version }, ct), HttpStatusCode.Conflict, null, ct);

        // A new number: the login follows it, the old one no longer signs in; the number cannot be removed.
        var renumbered = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}/whatsapp", new { whatsAppNumber = "0512343002", notificationsEnabled = true }, ct), ct);
        renumbered.ToString().ShouldNotContain("512343002");
        using (var session = ApiSession.Create(w.Factory))
        {
            await FailsAsync(BarberLoginTests.SignInAsync(session, "0512343001", BarberLoginTests.Chosen, ct), HttpStatusCode.Unauthorized, "auth.invalid_credentials", ct);
            await OkAsync(BarberLoginTests.SignInAsync(session, "0512343002", BarberLoginTests.Chosen, ct), ct);
        }

        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}/whatsapp", new { whatsAppNumber = (string?)null, notificationsEnabled = false }, ct),
            HttpStatusCode.BadRequest, "validation.required", ct);
        var quiet = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}/whatsapp",
            new { whatsAppNumber = (string?)null, notificationsEnabled = false, keepCurrentNumber = true }, ct), ct);
        quiet.GetProperty("whatsApp").GetProperty("notificationsEnabled").GetBoolean().ShouldBeFalse();
        quiet.GetProperty("whatsApp").GetProperty("masked").GetString().ShouldNotBeNullOrEmpty();

        // The services the barber does: the shop's own only.
        var services = await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{id}/services", ct), ct);
        services.GetProperty("services").EnumerateArray().ShouldAllBe(s => !s.GetProperty("assigned").GetBoolean());
        services.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("serviceId").GetGuid()).ShouldContain(w.Haircut);
        var assigned = await OkAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}/services", new { serviceIds = new[] { w.Haircut } }, ct), ct);
        assigned.GetProperty("services").EnumerateArray().Where(s => s.GetProperty("assigned").GetBoolean()).Select(s => s.GetProperty("serviceId").GetGuid()).ShouldBe([w.Haircut]);
        await FailsAsync(w.OwnerA.PutAsync($"/api/v1/shop/professionals/{id}/services", new { serviceIds = new[] { w.ServiceB } }, ct), HttpStatusCode.BadRequest, null, ct);
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/services/{w.Haircut}", ct), ct)).GetProperty("professionalIds").EnumerateArray()
            .Select(p => p.GetGuid()).ShouldContain(id, "the service page shows the same choice (D-137)");

        // A photo, then none.
        using (var avatar = await w.OwnerA.UploadAsync(HttpMethod.Put, $"/api/v1/shop/professionals/{id}/avatar", TestImages.Png(256, 256), ct))
        {
            avatar.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await avatar.JsonAsync(ct)).GetProperty("avatarUrl").GetString().ShouldNotBeNullOrEmpty();
        }

        (await OkAsync(w.OwnerA.DeleteAsync($"/api/v1/shop/professionals/{id}/avatar", ct), ct)).GetProperty("avatarUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        // Disabled: no longer bookable and signed out; enabled again, the login stays off until the owner turns it on.
        using var saad = await BarberLoginTests.SignInBarberAsync(w.Factory, "0512343002", ct);
        (await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/professionals/{id}/disable", null, ct), ct)).GetProperty("status").GetString().ShouldBe("Disabled");
        await FailsAsync(saad.GetAsync("/api/v1/me", ct), HttpStatusCode.Unauthorized, null, ct);
        await FailsAsync(w.OwnerA.PostAsync($"/api/v1/shop/professionals/{id}/disable", null, ct), HttpStatusCode.Conflict, null, ct);
        (await OkAsync(w.OwnerA.PostAsync($"/api/v1/shop/professionals/{id}/enable", null, ct), ct)).GetProperty("status").GetString().ShouldBe("Active");
        (await OkAsync(w.OwnerA.GetAsync($"/api/v1/shop/professionals/{id}/login", ct), ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
        var fresh = await BarberLoginTests.TurnOnAsync(w.OwnerA, $"/api/v1/shop/professionals/{id}/login", ct);
        using (var back = ApiSession.Create(w.Factory))
        {
            await OkAsync(back.PostAsync("/api/v1/auth/password/change-temporary", new { phone = "0512343002", password = fresh, newPassword = BarberLoginTests.Chosen }, ct), ct);
        }

        (await AuditAsync(w.Factory, id, ct)).Select(e => e.Action).Distinct().ShouldBe(
            ["professional.created", "barber_login.enabled", "professional.updated", "professional.whatsapp_changed", "professional.services_assigned",
             "professional.avatar_changed", "professional.disabled", "professional.enabled"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task TheOwnersBarberContract_HasNoShopField_AndNoReveal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("shop_barber_openapi", ct));
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct));

        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "CreateShopBarberRequest", "UpdateShopBarberRequest", "ShopBarberResponse" })
        {
            schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(p => p.Name).ShouldNotContain("shopId");
        }

        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(p => p.StartsWith("/api/v1/shop/professionals", StringComparison.Ordinal)).ToList();
        paths.ShouldNotContain(p => p.Contains("reveal", StringComparison.OrdinalIgnoreCase));
        json.RootElement.GetProperty("paths").GetProperty("/api/v1/shop/professionals/{professionalId}").EnumerateObject().Select(o => o.Name)
            .ShouldNotContain("delete", "a barber with history is disabled, never deleted");
    }
}
