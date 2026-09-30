using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Shouldly;
using Trimme.BuildingBlocks.Application.Notifications;
using Trimme.Modules.Notifications;

namespace Trimme.ArchitectureTests;

/// <summary>
/// Notification guard rails (Phase 15). Each rule also runs against a deliberately violating probe in this assembly, so a
/// rule that silently matches nothing fails.
/// </summary>
public sealed partial class NotificationRules
{
    private static readonly Assembly[] ProductionAssemblies = [.. Trimme.Api.ModuleCatalog.All.Select(m => m.Assembly)];

    /// <summary>
    /// The readers that return a customer's or a professional's phone number are used only by background jobs
    /// (<c>*.Jobs</c> namespaces), never by an endpoint or a use case that answers a request (spec §7, §8).
    /// </summary>
    [Fact]
    public void ContactReaders_AreUsedOnlyByNotificationJobs()
    {
        foreach (var reader in new[] { typeof(ICustomerContactReader), typeof(IProfessionalContactReader) })
        {
            UsersOutsideJobs(ProductionAssemblies, reader).ShouldBeEmpty($"{reader.Name} may only be used by *.Jobs types.");
            UsersOutsideJobs([typeof(NotificationRules).Assembly], reader).ShouldContain(typeof(ProbeContactReaderUser).FullName);
        }
    }

    /// <summary>
    /// R-NEG-09: the Notifications module's handlers and jobs render messages from the stored templates only. No string
    /// literal in its <c>Application</c> or <c>Jobs</c> namespaces holds Arabic text or template syntax; the default wording
    /// lives in the seeding namespace (<c>DefaultTemplates</c>).
    /// </summary>
    [Fact]
    public void Notifications_Handlers_DoNotContainMessageLiterals()
    {
        MessageLiterals(typeof(NotificationsModule).Assembly).ShouldBeEmpty();
        MessageLiterals(typeof(NotificationRules).Assembly).ShouldContain(l => l.StartsWith(typeof(Probe.Application.ProbeHardcodedMessage).FullName!, StringComparison.Ordinal));
    }

    private static List<string?> UsersOutsideJobs(IEnumerable<Assembly> assemblies, Type reader) =>
    [
        .. Types.InAssemblies(assemblies)
            .That().HaveDependencyOnAny(reader.FullName!)
            .GetTypes()
            .Select(t => t.ReflectionType)
            .Where(t => t != reader && !reader.IsAssignableFrom(t) && !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Where(t => !JobsNamespace().IsMatch(t.Namespace ?? string.Empty))
            .Select(t => t.FullName),
    ];

    private static List<string> MessageLiterals(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return
        [
            .. module.GetTypes()
                .Where(t => HandlerNamespace().IsMatch(NamespaceOf(t)))
                .SelectMany(t => t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions
                    .Where(i => i.OpCode == OpCodes.Ldstr && i.Operand is string text && (ArabicText().IsMatch(text) || text.Contains("{{", StringComparison.Ordinal)))
                    .Select(i => $"{t.FullName}.{m.Name}: {i.Operand}"))),
        ];
    }

    private static string NamespaceOf(TypeDefinition type) => type.DeclaringType is null ? type.Namespace : NamespaceOf(type.DeclaringType);

    [GeneratedRegex(@"\.Jobs(\.|$)")]
    private static partial Regex JobsNamespace();

    [GeneratedRegex(@"\.(Application|Jobs)(\.|$)")]
    private static partial Regex HandlerNamespace();

    [GeneratedRegex(@"[؀-ۿ]")]
    private static partial Regex ArabicText();
}

/// <summary>Reads a customer's number outside a job: exactly what the rule must reject.</summary>
internal sealed class ProbeContactReaderUser(ICustomerContactReader customers, IProfessionalContactReader professionals)
{
    public Task<CustomerContact?> Customer(Guid id) => customers.FindAsync(id, CancellationToken.None);

    public Task<ProfessionalContactCard?> Professional(Trimme.BuildingBlocks.Domain.Tenancy.ProfessionalId id) => professionals.FindAsync(id, CancellationToken.None);
}
