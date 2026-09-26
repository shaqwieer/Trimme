using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// EF caches one model per context type. <see cref="TrimmeDbContext"/>'s model depends on its contributors, so the key
/// includes them: tests that add test-only modules never receive (or poison) the production model.
/// </summary>
internal sealed class TrimmeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TrimmeDbContext trimme
            ? (context.GetType(), trimme.ModelSignature, designTime)
            : (context.GetType(), designTime);
}
