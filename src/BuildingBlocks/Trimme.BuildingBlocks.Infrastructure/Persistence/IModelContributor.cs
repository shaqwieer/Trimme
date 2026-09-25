using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// A module's contribution to the shared <see cref="TrimmeDbContext"/> model (D-038).
/// Each module owns one PostgreSQL schema and applies only its own entity configurations.
/// </summary>
public interface IModelContributor
{
    /// <summary>PostgreSQL schema owned by the module (snake_case, e.g. <c>shops</c>).</summary>
    string Schema { get; }

    /// <summary>Assembly scanned for strongly typed ID types and entity configurations.</summary>
    Assembly Assembly { get; }

    void ConfigureModel(ModelBuilder modelBuilder);
}
