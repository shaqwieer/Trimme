using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shouldly;
using Trimme.Modules.Identity.Domain;
using Trimme.Modules.Notifications.Domain;
using Trimme.Modules.Notifications.Infrastructure;

namespace Trimme.UnitTests.Notifications;

/// <summary>
/// D-140: the templates registered with Meta follow Meta's rules, number their parameters in the order the provider sends
/// them, and match the committed <c>docs/whatsapp/meta-templates.json</c> (regenerate it with
/// <c>TRIMME_UPDATE_SNAPSHOTS=1</c>).
/// </summary>
public sealed partial class MetaTemplateCatalogTests
{
    private const string SnapshotPath = "docs/whatsapp/meta-templates.json";
    private static readonly DateTimeOffset SampleAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void Catalog_HasEveryUtilitySlot_AndTheAuthenticationTemplateInBothLanguages()
    {
        var templates = Templates();
        templates.Count(t => Category(t) == "UTILITY").ShouldBe(20);
        templates.Where(t => Category(t) == "AUTHENTICATION").Select(t => (Name(t), t["language"]!.GetValue<string>()))
            .ShouldBe([(MetaTemplateCatalog.AuthenticationTemplateName, "ar"), (MetaTemplateCatalog.AuthenticationTemplateName, "en")]);
        templates.ShouldAllBe(t => MetaName().IsMatch(Name(t)));
        templates.Select(t => (Name(t), t["language"]!.GetValue<string>())).Distinct().Count().ShouldBe(templates.Count);
        MetaTemplateCatalog.CodeExpirationMinutes.ShouldBe((int)new OtpPolicy().CodeLifetime.TotalMinutes);
    }

    [Fact]
    public void UtilityBodies_FollowMetasRules_AndNumberParametersInTheSendersOrder()
    {
        foreach (var template in Templates().Where(t => Category(t) == "UTILITY"))
        {
            var body = Component(template, "BODY")!;
            var text = body["text"]!.GetValue<string>().Trim();
            var label = $"{Name(template)} ({template["language"]})";

            text.Length.ShouldBeLessThanOrEqualTo(TemplateValidator.MaxBodyLength, label);
            LeadingVariable().IsMatch(text).ShouldBeFalse($"{label}: Meta refuses a body that starts with a variable");
            TrailingVariable().IsMatch(text).ShouldBeFalse($"{label}: Meta refuses a body that ends with a variable");

            var numbers = Variable().Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
            numbers.ShouldBe(Enumerable.Range(1, numbers.Count).ToList(), $"{label}: {{{{1}}}}…{{{{n}}}} in order of first appearance");

            // Meta's "too many variables for its length" (subcode 2388293): it refused 2 words per variable and accepted
            // 2.67 (2026-11-05), so keep at least 2.5 words of fixed text per variable.
            var words = Word().Count(Variable().Replace(text, " "));
            (words * 2).ShouldBeGreaterThanOrEqualTo(Variable().Count(text) * 5, $"{label}: {words} words for {Variable().Count(text)} variables");
            var examples = body["example"]?["body_text"]?[0]?.AsArray() ?? [];
            examples.Count.ShouldBe(numbers.Count, label);
            examples.ShouldAllBe(e => e!.GetValue<string>().Length > 0 && !e.GetValue<string>().Contains('\n'));

            foreach (var button in Component(template, "BUTTONS")?["buttons"]?.AsArray() ?? [])
            {
                button!["text"]!.GetValue<string>().Length.ShouldBeLessThanOrEqualTo(TemplateValidator.MaxButtonLabelLength, label);
                button["url"]!.GetValue<string>().ShouldBe("https://groomhere.sa/{{1}}");
                button["example"]![0]!.GetValue<string>().ShouldStartWith("https://groomhere.sa/");
            }
        }
    }

    [Fact]
    public void NumberedBody_ReusesTheNumberOfARepeatedPlaceholder()
    {
        MetaTemplateCatalog.NumberedBody("Hi {{customer_name}}, {{shop_name}} awaits you, {{ customer_name }}.")
            .ShouldBe("Hi {{1}}, {{2}} awaits you, {{1}}.");
    }

    [Fact]
    public void Snapshot_MatchesTheCommittedFile()
    {
        var generated = MetaTemplateCatalog.Build("https://groomhere.sa/", SampleAt).ToJsonString(SnapshotJson) + "\n";
        var path = Path.Combine(RepositoryRoot(), SnapshotPath);
        if (Environment.GetEnvironmentVariable("TRIMME_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
        }

        File.Exists(path).ShouldBeTrue($"{SnapshotPath} is missing; run the unit tests with TRIMME_UPDATE_SNAPSHOTS=1");
        File.ReadAllText(path).ReplaceLineEndings("\n").ShouldBe(generated, $"{SnapshotPath} is stale; run the unit tests with TRIMME_UPDATE_SNAPSHOTS=1");
    }

    private static List<JsonObject> Templates() =>
        [.. MetaTemplateCatalog.Build("https://groomhere.sa/", SampleAt).Select(t => t!.AsObject())];

    private static string Name(JsonObject template) => template["name"]!.GetValue<string>();

    private static string Category(JsonObject template) => template["category"]!.GetValue<string>();

    private static JsonObject? Component(JsonObject template, string type) =>
        template["components"]!.AsArray().Select(c => c!.AsObject()).FirstOrDefault(c => c["type"]!.GetValue<string>() == type);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (global.json) was not found.");
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex MetaName();

    [GeneratedRegex(@"\{\{(\d+)\}\}")]
    private static partial Regex Variable();

    /// <summary>Meta counts punctuation as nothing: "{{1}}." still ends with a variable (subcode 2388299).</summary>
    [GeneratedRegex(@"^[^\p{L}]*\{\{\d+\}\}")]
    private static partial Regex LeadingVariable();

    [GeneratedRegex(@"\{\{\d+\}\}[^\p{L}]*$")]
    private static partial Regex TrailingVariable();

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’]*")]
    private static partial Regex Word();
}
