using Shouldly;
using Trimme.Modules.Identity.Domain;

namespace Trimme.UnitTests.Identity;

/// <summary>Keeps <c>docs/permissions-matrix.md</c> in step with the code catalogue and the seed roles.</summary>
public sealed class PermissionsMatrixDocTests
{
    [Fact]
    public void Every_catalogue_permission_and_seed_role_is_documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "permissions-matrix.md"));

        Permissions.All.Select(p => p.Code).Where(code => !doc.Contains($"`{code}`", StringComparison.Ordinal))
            .ShouldBeEmpty("Add the missing permissions to docs/permissions-matrix.md.");
        SystemRoles.All.Select(r => r.Name).Where(role => !doc.Contains(role, StringComparison.Ordinal))
            .ShouldBeEmpty("Add the missing roles to docs/permissions-matrix.md.");
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
}
