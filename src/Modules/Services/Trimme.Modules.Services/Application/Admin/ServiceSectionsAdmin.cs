using FluentValidation;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Services.Application.Admin;

// The admin's work on one shop's sections (D-149), from the shop's admin page: the same steps as the owner's, inside the
// admin scope, for a shop that exists.

internal sealed record ListShopServiceSectionsQuery(Guid ShopId) : IQuery<IReadOnlyList<ServiceSectionResponse>?>;

internal sealed record CreateShopServiceSectionCommand(Guid ShopId, string NameAr, string? NameEn) : ICommand<Result<ServiceSectionResponse>>, IServiceSectionFields;

internal sealed record RenameShopServiceSectionCommand(Guid ShopId, Guid SectionId, string NameAr, string? NameEn)
    : ICommand<Result<ServiceSectionResponse>>, IServiceSectionFields;

internal sealed record DeleteShopServiceSectionCommand(Guid ShopId, Guid SectionId) : ICommand<Result>;

internal sealed record OrderShopServiceSectionsCommand(Guid ShopId, IReadOnlyList<Guid> OrderedIds) : ICommand<Result<IReadOnlyList<ServiceSectionResponse>>>;

internal sealed class CreateShopServiceSectionValidator : AbstractValidator<CreateShopServiceSectionCommand>
{
    public CreateShopServiceSectionValidator() => Include(new ServiceSectionRules<CreateShopServiceSectionCommand>());
}

internal sealed class RenameShopServiceSectionValidator : AbstractValidator<RenameShopServiceSectionCommand>
{
    public RenameShopServiceSectionValidator() => Include(new ServiceSectionRules<RenameShopServiceSectionCommand>());
}

/// <summary>The admin's section work for one shop: inside the admin scope, a shop that exists.</summary>
internal sealed class ListShopServiceSectionsHandler(IAdminDataScope scope, IShopDirectory shops, ServiceSectionStore store)
    : IQueryHandler<ListShopServiceSectionsQuery, IReadOnlyList<ServiceSectionResponse>?>
{
    public async Task<IReadOnlyList<ServiceSectionResponse>?> Handle(ListShopServiceSectionsQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(query.ShopId);
        return await shops.FindAsync(shopId, cancellationToken) is null ? null : await store.ListAsync(shopId, cancellationToken);
    }
}

internal sealed class CreateShopServiceSectionHandler(IAdminDataScope scope, IShopDirectory shops, ServiceSectionStore store)
    : ICommandHandler<CreateShopServiceSectionCommand, Result<ServiceSectionResponse>>
{
    public async Task<Result<ServiceSectionResponse>> Handle(CreateShopServiceSectionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        var shopId = new ShopId(command.ShopId);
        return await shops.FindAsync(shopId, cancellationToken) is null
            ? Error.NotFound("shop.not_found", "The shop was not found.")
            : await store.CreateAsync(shopId, command, cancellationToken);
    }
}

internal sealed class RenameShopServiceSectionHandler(IAdminDataScope scope, ServiceSectionStore store)
    : ICommandHandler<RenameShopServiceSectionCommand, Result<ServiceSectionResponse>>
{
    public async Task<Result<ServiceSectionResponse>> Handle(RenameShopServiceSectionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await store.RenameAsync(new ShopId(command.ShopId), command.SectionId, command, cancellationToken);
    }
}

internal sealed class DeleteShopServiceSectionHandler(IAdminDataScope scope, ServiceSectionStore store) : ICommandHandler<DeleteShopServiceSectionCommand, Result>
{
    public async Task<Result> Handle(DeleteShopServiceSectionCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await store.DeleteAsync(new ShopId(command.ShopId), command.SectionId, cancellationToken);
    }
}

internal sealed class OrderShopServiceSectionsHandler(IAdminDataScope scope, ServiceSectionStore store)
    : ICommandHandler<OrderShopServiceSectionsCommand, Result<IReadOnlyList<ServiceSectionResponse>>>
{
    public async Task<Result<IReadOnlyList<ServiceSectionResponse>>> Handle(OrderShopServiceSectionsCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await store.OrderAsync(new ShopId(command.ShopId), command.OrderedIds, cancellationToken);
    }
}
