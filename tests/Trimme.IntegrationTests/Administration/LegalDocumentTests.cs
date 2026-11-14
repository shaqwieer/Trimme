using System.Net;
using Npgsql;
using Shouldly;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Administration;

/// <summary>
/// D-144: the admin writes the terms and the privacy policy in rich text, per language. Only the allowlisted document
/// structure is stored; the public read follows every save at once; resetting brings back the built-in text.
/// </summary>
public sealed class LegalDocumentTests(PostgresFixture postgres)
{
    private static object Doc(params object[] blocks) => new { type = "doc", content = blocks };

    private static object Text(string text, params object[] marks) => marks.Length == 0 ? new { type = "text", text } : new { type = "text", text, marks };

    [Fact]
    public async Task TheAdminsRichText_IsCleanedSaved_AndServedPublicly_PerLanguage_UntilReset()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await IdentityTestData.CreateFactoryAsync(postgres, "legal_documents", ct);
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        using var anonymous = ApiSession.Create(factory);

        // Nothing saved: the public page uses its built-in text.
        (await anonymous.GetAsync("/api/v1/public/legal/terms?locale=ar", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var document = Doc(
            new { type = "heading", attrs = new { level = 1, textAlign = "right" }, content = new[] { Text("الخدمة") } },
            new
            {
                type = "paragraph",
                content = new object[]
                {
                    Text("اقرأ "),
                    Text("سياسة الإلغاء", new { type = "bold" }, new { type = "link", attrs = new { href = "https://groomhere.sa/ar/terms", target = "_blank", @class = "x" } }),
                    new { type = "hardBreak" },
                    Text("بعناية."),
                },
            },
            new { type = "bulletList", content = new[] { new { type = "listItem", content = new[] { new { type = "paragraph", content = new[] { Text("بند") } } } } } });

        using (var saved = await admin.PutAsync("/api/v1/admin/legal/terms/ar", new { content = document }, ct))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
            var body = await saved.JsonAsync(ct);
            body.GetProperty("kind").GetString().ShouldBe("terms");
            var json = body.GetProperty("content").GetRawText();
            // Only allowlisted attributes survive; the page's own heading is the h1, so sections start at h2.
            json.ShouldNotContain("target");
            json.ShouldNotContain("textAlign");
            json.ShouldNotContain("class");
            body.GetProperty("content").GetProperty("content")[0].GetProperty("attrs").GetProperty("level").GetInt32().ShouldBe(2);
        }

        var published = await (await anonymous.GetAsync("/api/v1/public/legal/terms?locale=ar", ct)).JsonAsync(ct);
        published.GetProperty("content").GetRawText().ShouldContain("سياسة الإلغاء");
        (await anonymous.GetAsync("/api/v1/public/legal/terms?locale=en", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/v1/public/legal/privacy?locale=ar", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A second save is what the public read serves next (the public cache is evicted).
        await (await admin.PutAsync("/api/v1/admin/legal/terms/ar", new { content = Doc(new { type = "paragraph", content = new[] { Text("نص جديد") } }) }, ct))
            .JsonAsync(ct);
        (await (await anonymous.GetAsync("/api/v1/public/legal/terms?locale=ar", ct)).JsonAsync(ct)).GetProperty("content").GetRawText().ShouldContain("نص جديد");

        // Anything outside the allowlist refuses the save.
        foreach (var bad in new[]
                 {
                     Doc(new { type = "paragraph", content = new[] { Text("x", new { type = "link", attrs = new { href = "javascript:alert(1)" } }) } }),
                     Doc(new { type = "image", attrs = new { src = "https://evil.example/x.png" } }),
                     Doc(new { type = "paragraph", content = new[] { Text("x", new { type = "textStyle", attrs = new { color = "red" } }) } }),
                     Doc(new { type = "paragraph" }),
                     (object)new { type = "paragraph", content = new[] { Text("not a doc") } },
                 })
        {
            using var refused = await admin.PutAsync("/api/v1/admin/legal/privacy/ar", new { content = bad }, ct);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync(ct)).ShouldContain("legal.content_invalid");
        }

        (await admin.PutAsync("/api/v1/admin/legal/cookies/ar", new { content = Doc(new { type = "paragraph", content = new[] { Text("x") } }) }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PutAsync("/api/v1/admin/legal/terms/fr", new { content = Doc(new { type = "paragraph", content = new[] { Text("x") } }) }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await (await admin.GetAsync("/api/v1/admin/legal", ct)).JsonAsync(ct);
        list.EnumerateArray().Select(d => $"{d.GetProperty("kind").GetString()}:{d.GetProperty("locale").GetString()}").ShouldBe(["terms:ar"]);

        // Reset: the built-in text again.
        (await admin.DeleteAsync("/api/v1/admin/legal/terms/ar", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync("/api/v1/public/legal/terms?locale=ar", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var count = new NpgsqlCommand(
            "SELECT string_agg(action, ',' ORDER BY sequence) FROM administration.audit_entries WHERE entity_type = 'LegalDocument'", connection);
        ((string)(await count.ExecuteScalarAsync(ct))!).ShouldBe("legal_document.saved,legal_document.saved,legal_document.reset");

        // Shop accounts and customers cannot write them.
        using var customer = await IdentityTestData.SignInCustomerAsync(factory, IdentityTestData.NewPhone(), ct);
        (await customer.PutAsync("/api/v1/admin/legal/terms/ar", new { content = Doc(new { type = "paragraph", content = new[] { Text("x") } }) }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
