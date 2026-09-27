using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.IntegrationTests.Tenancy;
using Trimme.Modules.Administration.Domain;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Professionals.Domain;
using Trimme.Tests.Shared;

namespace Trimme.IntegrationTests.Professionals;

/// <summary>
/// Professionals (R-PRO-01/02, R-NEG-01/05/06): one shop each, fixed forever; protected WhatsApp numbers; no transfer.
/// </summary>
public sealed class ProfessionalTests(PostgresFixture postgres)
{
    private const string Number = "0501234567";
    private const string E164 = "+966501234567";

    private static async Task<JsonElement> CreateAsync(ApiSession admin, Guid shopId, string nameEn, CancellationToken ct, string? whatsApp = Number, bool notifications = true)
    {
        using var created = await admin.PostAsync("/api/v1/admin/professionals", new
        {
            shopId,
            nameAr = "فيصل القحطاني",
            nameEn,
            specialtyAr = "تدريج",
            specialtyEn = "Fades",
            whatsAppNumber = whatsApp,
            notificationsEnabled = notifications,
        }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return await created.JsonAsync(ct);
    }

    [Fact]
    public async Task Admin_CreatesProfessional_WithMaskedWhatsApp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_create", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "pro-shop", ct);

        var professional = await CreateAsync(admin, shopId, "Faisal Al-Qahtani", ct);
        professional.GetProperty("shopId").GetGuid().ShouldBe(shopId);
        professional.GetProperty("slug").GetString().ShouldBe("faisal-al-qahtani");
        professional.GetProperty("status").GetString().ShouldBe("Active");
        var whatsApp = professional.GetProperty("whatsApp");
        whatsApp.GetProperty("masked").GetString().ShouldBe("+966 5•• ••• •67");
        whatsApp.GetProperty("notificationsEnabled").GetBoolean().ShouldBeTrue();
        professional.GetRawText().ShouldNotContain("501234567");

        // The same English name gets a free slug in the same shop.
        (await CreateAsync(admin, shopId, "Faisal Al-Qahtani", ct, whatsApp: null, notifications: false)).GetProperty("slug").GetString().ShouldBe("faisal-al-qahtani-2");

        var list = await (await admin.GetAsync($"/api/v1/admin/professionals?shopId={shopId}", ct)).JsonAsync(ct);
        list.GetProperty("total").GetInt32().ShouldBe(2);
        list.GetRawText().ShouldNotContain("501234567");
        list.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("whatsApp").GetProperty("masked").GetString() == "+966 5•• ••• •67");

        // Validation: invalid number; notifications without a number; unknown shop.
        using (var invalid = await admin.PostAsync("/api/v1/admin/professionals", new { shopId, nameAr = "س", nameEn = "S", whatsAppNumber = "0112345678", notificationsEnabled = false }, ct))
        {
            (await invalid.JsonAsync(ct)).GetProperty("errors").GetProperty("whatsAppNumber")[0].GetString().ShouldBe("validation.phone_invalid");
        }

        using (var noNumber = await admin.PostAsync("/api/v1/admin/professionals", new { shopId, nameAr = "س", nameEn = "S", notificationsEnabled = true }, ct))
        {
            (await noNumber.JsonAsync(ct)).GetProperty("errors").GetProperty("notificationsEnabled")[0].GetString().ShouldBe("validation.whatsapp_required");
        }

        using (var unknownShop = await admin.PostAsync("/api/v1/admin/professionals", new { shopId = Guid.NewGuid(), nameAr = "س", nameEn = "S" }, ct))
        {
            (await unknownShop.JsonAsync(ct)).GetProperty("errors").GetProperty("shopId")[0].GetString().ShouldBe("validation.invalid");
        }

        // Stored encrypted with a lookup hash; the audit trail never holds the number.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using (var command = new NpgsqlCommand("SELECT protected_whatsapp, whatsapp_lookup_hash, whatsapp_masked FROM professionals.professional_contacts WHERE professional_id = @id", connection))
        {
            command.Parameters.AddWithValue("id", professional.GetProperty("id").GetGuid());
            await using var reader = await command.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).ShouldBeTrue();
            reader.GetString(0).ShouldNotContain("501234567");
            reader.GetString(1).Length.ShouldBe(64);
            reader.GetString(2).ShouldBe("+966 5•• ••• •67");
        }

        var audit = await db.Set<AuditEntry>().Where(e => e.Action.StartsWith("professional.")).ToListAsync(ct);
        audit.ShouldNotBeEmpty();
        audit.ShouldAllBe(e => e.Summary == null || !e.Summary.Contains("501234567"));
    }

    [Fact]
    public async Task Reveal_RequiresPermissionAndReason_AndIsAudited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_reveal", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var support = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.Support, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "reveal-shop", ct);
        var id = (await CreateAsync(admin, shopId, "Sultan", ct)).GetProperty("id").GetGuid();

        using (var forbidden = await support.PostAsync($"/api/v1/admin/professionals/{id}/whatsapp/reveal", new { reason = "Customer complaint follow-up" }, ct))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "Support can view professionals but not reveal numbers");
        }

        using (var noReason = await admin.PostAsync($"/api/v1/admin/professionals/{id}/whatsapp/reveal", new { reason = " " }, ct))
        {
            (await noReason.JsonAsync(ct)).GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("validation.reason_required");
        }

        using (var revealed = await admin.PostAsync($"/api/v1/admin/professionals/{id}/whatsapp/reveal", new { reason = "Confirm the number by phone" }, ct))
        {
            revealed.StatusCode.ShouldBe(HttpStatusCode.OK);
            revealed.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await revealed.JsonAsync(ct)).GetProperty("number").GetString().ShouldBe(E164);
        }

        // Changing only the toggle keeps the number without revealing it.
        using (var toggled = await admin.PutAsync($"/api/v1/admin/professionals/{id}/whatsapp", new { notificationsEnabled = false, keepCurrentNumber = true }, ct))
        {
            var whatsApp = (await toggled.JsonAsync(ct)).GetProperty("whatsApp");
            whatsApp.GetProperty("masked").GetString().ShouldBe("+966 5•• ••• •67");
            whatsApp.GetProperty("notificationsEnabled").GetBoolean().ShouldBeFalse();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var entry = await db.Set<AuditEntry>().SingleAsync(e => e.Action == "professional.whatsapp_revealed", ct);
        entry.EntityId.ShouldBe(id.ToString());
        entry.Reason.ShouldBe("Confirm the number by phone");
        entry.ShopId.ShouldBe(shopId);
    }

    [Fact]
    public async Task PublicAndShopProfessionalDtos_HaveNoPhone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_privacy", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        await CreateAsync(admin, shops.A.ShopId, "Faisal", ct);
        var slugA = (await (await admin.GetAsync($"/api/v1/admin/shops/{shops.A.ShopId}", ct)).JsonAsync(ct)).GetProperty("slug").GetString()!;

        using var anonymous = ApiSession.Create(factory);
        using var publicList = await anonymous.GetAsync($"/api/v1/public/shops/{slugA}/professionals", ct);
        publicList.StatusCode.ShouldBe(HttpStatusCode.OK);
        var publicJson = await publicList.Content.ReadAsStringAsync(ct);
        JsonDocument.Parse(publicJson).RootElement.GetArrayLength().ShouldBe(1);

        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        var shopJson = await (await owner.GetAsync("/api/v1/shop/professionals", ct)).Content.ReadAsStringAsync(ct);
        JsonDocument.Parse(shopJson).RootElement.GetArrayLength().ShouldBe(1);

        foreach (var json in new[] { publicJson, shopJson })
        {
            json.ShouldNotContain("501234567");
            json.ShouldNotContain("+966");
            json.ShouldNotContain("•");
            json.ShouldNotContain("whatsapp", Case.Insensitive);
        }

        // The public contracts carry no phone-like member at all.
        foreach (var type in new[] { typeof(Trimme.Modules.Professionals.Application.Public.PublicProfessionalResponse), typeof(Trimme.Modules.Professionals.Application.ShopProfessionalResponse) })
        {
            type.GetProperties().Select(p => p.Name).ShouldNotContain(name => name.Contains("phone", StringComparison.OrdinalIgnoreCase) || name.Contains("whatsapp", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Professional_ShopId_IsImmutable_AndTheUpdateContractCannotCarryIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_immutable", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var professional = await CreateAsync(admin, shops.A.ShopId, "Rakan", ct);
        var id = professional.GetProperty("id").GetGuid();

        // An extra "shopId" in the update body is ignored: the contract has no such field.
        using (var update = await admin.PutAsync($"/api/v1/admin/professionals/{id}", new
        {
            shopId = shops.B.ShopId,
            nameAr = "راكان المطيري",
            nameEn = "Rakan Al-Mutairi",
            version = professional.GetProperty("version").GetUInt32(),
        }, ct))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync(ct));
            var body = await update.JsonAsync(ct);
            body.GetProperty("shopId").GetGuid().ShouldBe(shops.A.ShopId);
            body.GetProperty("nameEn").GetString().ShouldBe("Rakan Al-Mutairi");
        }

        // Stale version → 409.
        using (var stale = await admin.PutAsync($"/api/v1/admin/professionals/{id}", new { nameAr = "س", nameEn = "S", version = professional.GetProperty("version").GetUInt32() }, ct))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Even with the tenant filter lifted, the persistence layer refuses to move the row: ShopId is part of the
        // (shop_id, id) key, so EF will not mark it modified, and the tenant rules reject any ShopId change.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        using (scope.ServiceProvider.GetRequiredService<ISystemDataScope>().Begin())
        {
            var entity = await db.Set<Professional>().SingleAsync(p => p.Id == new ProfessionalId(id), ct);
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                db.Entry(entity).Property(p => p.ShopId).CurrentValue = new ShopId(shops.B.ShopId);
                await db.SaveChangesAsync(ct);
            });
        }

        (await (await admin.GetAsync($"/api/v1/admin/professionals/{id}", ct)).JsonAsync(ct)).GetProperty("shopId").GetGuid().ShouldBe(shops.A.ShopId);
    }

    [Fact]
    public async Task Professional_BelongsToExactlyOneShop()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_one_shop", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var id = (await CreateAsync(admin, shops.A.ShopId, "Omar", ct)).GetProperty("id").GetGuid();

        // The same person (number) cannot be listed again as a professional of another shop.
        using (var shared = await admin.PostAsync("/api/v1/admin/professionals", new { shopId = shops.B.ShopId, nameAr = "عمر", nameEn = "Omar", whatsAppNumber = E164, notificationsEnabled = true }, ct))
        {
            (await shared.JsonAsync(ct)).GetProperty("errors").GetProperty("whatsAppNumber")[0].GetString().ShouldBe("validation.whatsapp_taken");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        // shop_id is required.
        await using (var noShop = new NpgsqlCommand("INSERT INTO professionals.professionals (id, shop_id, slug, name_ar, name_en, status, created_at) VALUES (gen_random_uuid(), NULL, 'x-y', 'س', 'S', 'Active', now())", connection))
        {
            (await Should.ThrowAsync<PostgresException>(() => noShop.ExecuteNonQueryAsync(ct))).SqlState.ShouldBe(PostgresErrorCodes.NotNullViolation);
        }

        // A contact row cannot pair the professional with another shop: the composite key (shop_id, professional_id) rejects it.
        await using (var crossShop = new NpgsqlCommand("UPDATE professionals.professional_contacts SET shop_id = @other WHERE professional_id = @id", connection))
        {
            crossShop.Parameters.AddWithValue("other", shops.B.ShopId);
            crossShop.Parameters.AddWithValue("id", id);
            (await Should.ThrowAsync<PostgresException>(() => crossShop.ExecuteNonQueryAsync(ct))).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    [Fact]
    public async Task Shop_CannotCreateOrChangeProfessionals_AndSeesOnlyItsOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_shop_side", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        var inA = (await CreateAsync(admin, shops.A.ShopId, "Faisal", ct)).GetProperty("id").GetGuid();
        await CreateAsync(admin, shops.B.ShopId, "Majed", ct, whatsApp: "0501234568");
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);

        using (var create = await owner.PostAsync("/api/v1/admin/professionals", new { shopId = shops.A.ShopId, nameAr = "س", nameEn = "S" }, ct))
        {
            create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var edit = await owner.PutAsync($"/api/v1/admin/professionals/{inA}", new { nameAr = "س", nameEn = "S", version = 0 }, ct))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var disable = await owner.PostAsync($"/api/v1/admin/professionals/{inA}/disable", new { }, ct))
        {
            disable.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var own = await (await owner.GetAsync("/api/v1/shop/professionals", ct)).JsonAsync(ct);
        own.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ShouldBe([inA]);
    }

    [Fact]
    public async Task DisabledProfessionals_LeaveThePublicPage_AndAvatarIsStored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "pro_disable", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shopId = await ShopTestData.CreateShopAsync(admin, "disable-shop", ct);
        var id = (await CreateAsync(admin, shopId, "Ziad", ct)).GetProperty("id").GetGuid();
        using var anonymous = ApiSession.Create(factory);

        using (var avatar = await admin.UploadAsync(HttpMethod.Put, $"/api/v1/admin/professionals/{id}/avatar", TestImages.Png(256, 256), ct))
        {
            avatar.StatusCode.ShouldBe(HttpStatusCode.OK);
            var url = (await avatar.JsonAsync(ct)).GetProperty("avatarUrl").GetString()!;
            (await anonymous.GetAsync(url, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await (await anonymous.GetAsync("/api/v1/public/shops/disable-shop/professionals", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(1);

        using (var disabled = await admin.PostAsync($"/api/v1/admin/professionals/{id}/disable", new { reason = "On leave" }, ct))
        {
            (await disabled.JsonAsync(ct)).GetProperty("status").GetString().ShouldBe("Disabled");
        }

        (await admin.PostAsync($"/api/v1/admin/professionals/{id}/disable", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await (await anonymous.GetAsync("/api/v1/public/shops/disable-shop/professionals", ct)).JsonAsync(ct)).GetArrayLength().ShouldBe(0);
        (await anonymous.GetAsync("/api/v1/public/shops/unknown-shop/professionals", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using (var number = await admin.PutAsync($"/api/v1/admin/professionals/{id}/whatsapp", new { whatsAppNumber = (string?)null, notificationsEnabled = false }, ct))
        {
            (await number.JsonAsync(ct)).GetProperty("whatsApp").GetProperty("masked").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrimmeDbContext>();
        (await db.Set<AuditEntry>().Where(e => e.EntityId == id.ToString()).Select(e => e.Action).ToListAsync(ct))
            .ShouldBe(["professional.created", "professional.avatar_changed", "professional.disabled", "professional.whatsapp_changed"], ignoreOrder: true);
    }

    [Fact]
    public async Task OpenApi_HasNoTransferOperation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new TrimmeApiFactory(await postgres.CreateDatabaseAsync("pro_openapi", ct));
        using var client = factory.CreateClient();
        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct);
        using var json = JsonDocument.Parse(document);

        var paths = json.RootElement.GetProperty("paths").EnumerateObject().ToList();
        paths.Select(p => p.Name).ShouldContain("/api/v1/admin/professionals");
        foreach (var path in paths)
        {
            path.Name.ShouldNotContain("transfer", Case.Insensitive);
            path.Name.ShouldNotContain("move", Case.Insensitive);
            foreach (var operation in path.Value.EnumerateObject())
            {
                operation.Value.GetProperty("operationId").GetString()!.ShouldNotContain("Transfer", Case.Insensitive);
            }
        }

        json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UpdateProfessionalRequest")
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).ShouldNotContain("shopId");
    }
}
