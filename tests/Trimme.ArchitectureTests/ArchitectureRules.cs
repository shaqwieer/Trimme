using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Trimme.Api;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Primitives;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Modules;

namespace Trimme.ArchitectureTests;

/// <summary>Dependency-direction and module-boundary rules (R-FND-01, D-038).</summary>
public sealed class ArchitectureRules
{
    private static readonly string[] FrameworkInfrastructure =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Serilog",
    ];

    private static readonly Assembly DomainBuildingBlock = typeof(IEntityId).Assembly;
    private static readonly Assembly ApplicationBuildingBlock = typeof(IDispatcher).Assembly;
    private static readonly Assembly InfrastructureBuildingBlock = typeof(TrimmeDbContext).Assembly;
    private static readonly Assembly WebBuildingBlock = typeof(IModule).Assembly;

    public static TheoryData<string> ModuleNames => new(ModuleCatalog.All.Select(m => m.Name));

    [Fact]
    public void All_twelve_modules_are_registered_and_non_empty()
    {
        ModuleCatalog.All.Count.ShouldBe(12);
        ModuleCatalog.All.Select(m => m.Schema).Distinct().Count().ShouldBe(12, "Every module owns a distinct schema.");
        ModuleCatalog.All.ShouldAllBe(m => m.Assembly.GetTypes().Length > 0);
    }

    [Fact]
    public void Domain_building_block_has_no_framework_or_outer_layer_dependencies()
    {
        AssertNoDependencies(
            Types.InAssembly(DomainBuildingBlock),
            [.. FrameworkInfrastructure, "FluentValidation", "Trimme.BuildingBlocks.Application", "Trimme.BuildingBlocks.Infrastructure", "Trimme.BuildingBlocks.Web", "Trimme.Modules"]);
    }

    [Fact]
    public void Application_building_block_does_not_depend_on_infrastructure_or_web()
    {
        AssertNoDependencies(
            Types.InAssembly(ApplicationBuildingBlock),
            [.. FrameworkInfrastructure, "Trimme.BuildingBlocks.Infrastructure", "Trimme.BuildingBlocks.Web", "Trimme.Modules"]);
    }

    [Fact]
    public void Infrastructure_building_block_does_not_depend_on_web_or_modules()
    {
        AssertNoDependencies(
            Types.InAssembly(InfrastructureBuildingBlock),
            ["Microsoft.AspNetCore", "Trimme.BuildingBlocks.Web", "Trimme.Modules"]);
    }

    [Fact]
    public void Web_building_block_does_not_depend_on_modules()
    {
        AssertNoDependencies(Types.InAssembly(WebBuildingBlock), ["Trimme.Modules"]);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_domain_namespace_is_persistence_and_transport_ignorant(string moduleName)
    {
        var assembly = ModuleAssembly(moduleName);
        var ns = $"Trimme.Modules.{moduleName}";

        AssertNoDependencies(
            Types.InAssembly(assembly).That().ResideInNamespace($"{ns}.Domain"),
            [.. FrameworkInfrastructure, "FluentValidation", $"{ns}.Application", $"{ns}.Infrastructure", $"{ns}.Api", "Trimme.BuildingBlocks.Infrastructure", "Trimme.BuildingBlocks.Web"]);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_application_namespace_does_not_depend_on_infrastructure_or_transport(string moduleName)
    {
        var assembly = ModuleAssembly(moduleName);
        var ns = $"Trimme.Modules.{moduleName}";

        AssertNoDependencies(
            Types.InAssembly(assembly).That().ResideInNamespace($"{ns}.Application"),
            ["Microsoft.AspNetCore", "Npgsql", $"{ns}.Infrastructure", $"{ns}.Api", "Trimme.BuildingBlocks.Web"]);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Modules_reference_other_modules_only_through_contracts(string moduleName)
    {
        var assembly = ModuleAssembly(moduleName);

        var forbidden = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Trimme.Modules.", StringComparison.Ordinal)
                           && !name.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToArray();

        forbidden.ShouldBeEmpty($"{assembly.GetName().Name} must reference other modules only via their .Contracts assemblies.");
    }

    /// <summary>
    /// Compiled metadata omits unused references, so the project files are checked as well:
    /// a module project may reference other modules only through their <c>.Contracts</c> projects.
    /// </summary>
    [Fact]
    public void Module_project_files_reference_other_modules_only_through_contracts()
    {
        var modulesRoot = Path.Combine(RepositoryRoot(), "src", "Modules");
        var projectFiles = Directory.GetFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories);
        projectFiles.Length.ShouldBeGreaterThanOrEqualTo(12);

        var violations = projectFiles
            .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith(".Contracts", StringComparison.Ordinal))
            .SelectMany(project => System.Xml.Linq.XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(r => r.Attribute("Include")?.Value ?? string.Empty)
                .Where(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')) is var name
                                  && name.StartsWith("Trimme.Modules.", StringComparison.Ordinal)
                                  && !name.EndsWith(".Contracts", StringComparison.Ordinal))
                .Select(include => $"{Path.GetFileName(project)} -> {include}"))
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_handlers_and_infrastructure_are_internal(string moduleName)
    {
        var assembly = ModuleAssembly(moduleName);
        var ns = $"Trimme.Modules.{moduleName}";

        var publicInternals = assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith($"{ns}.Infrastructure", StringComparison.Ordinal) == true
                        || t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .Select(t => t.FullName)
            .ToArray();

        publicInternals.ShouldBeEmpty("Handlers and infrastructure types must be internal to their module (D-038).");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trimme.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Trimme.slnx) not found.");
    }

    private static Assembly ModuleAssembly(string moduleName) =>
        ModuleCatalog.All.Single(m => m.Name == moduleName).Assembly;

    private static void AssertNoDependencies(PredicateList types, string[] forbidden)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static void AssertNoDependencies(Types types, string[] forbidden)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Forbidden dependencies found in: " + string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []);
}
