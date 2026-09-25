using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Trimme.BuildingBlocks.Domain.Primitives;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Maps a strongly typed ID (<c>readonly record struct XId(Guid Value)</c>) to a <c>uuid</c> column.
/// Built from the type's <c>Guid</c> constructor because expression trees cannot call static abstract members.
/// </summary>
public sealed class EntityIdValueConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IEntityId
{
    public EntityIdValueConverter()
        : base(id => id.Value, BuildFactory())
    {
    }

    private static Expression<Func<Guid, TId>> BuildFactory()
    {
        var constructor = typeof(TId).GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(Guid)])
            ?? throw new InvalidOperationException(
                $"{typeof(TId).Name} must declare a public constructor taking a single Guid to be used as an entity identifier.");

        var value = Expression.Parameter(typeof(Guid), "value");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, value), value);
    }
}
