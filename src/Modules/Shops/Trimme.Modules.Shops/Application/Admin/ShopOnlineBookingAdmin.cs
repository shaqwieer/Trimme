using FluentValidation;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Domain.Tenancy;
using Trimme.Modules.Shops.Domain;

namespace Trimme.Modules.Shops.Application.Admin;

// The admin pauses or resumes any shop's online booking (D-155, the client's 1-1: «للمالك والإدارة بس»), with the same
// rules as the owner's; the shop is told, and the audit trail says the platform did it.

internal sealed record AdminGetOnlineBookingStateQuery(Guid ShopId) : IQuery<OnlineBookingStateResponse?>;

internal sealed record AdminPauseOnlineBookingCommand(Guid ShopId, string? Reason, DateTimeOffset? ResumesAt) : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed record AdminResumeOnlineBookingCommand(Guid ShopId) : ICommand<Result<OnlineBookingStateResponse>>;

internal sealed class AdminPauseOnlineBookingValidator : AbstractValidator<AdminPauseOnlineBookingCommand>
{
    public AdminPauseOnlineBookingValidator() =>
        RuleFor(c => c.Reason).MaximumLength(OnlineBookingPause.MaxReasonLength).WithErrorCode("validation.too_long");
}

internal sealed class AdminGetOnlineBookingStateHandler(IAdminDataScope scope, OnlineBookingPauses pauses)
    : IQueryHandler<AdminGetOnlineBookingStateQuery, OnlineBookingStateResponse?>
{
    public async Task<OnlineBookingStateResponse?> Handle(AdminGetOnlineBookingStateQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await pauses.StateAsync(new ShopId(query.ShopId), cancellationToken);
    }
}

internal sealed class AdminPauseOnlineBookingHandler(IAdminDataScope scope, OnlineBookingPauses pauses)
    : ICommandHandler<AdminPauseOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(AdminPauseOnlineBookingCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await pauses.PauseAsync(new ShopId(command.ShopId), command.Reason, command.ResumesAt, byAdmin: true, cancellationToken);
    }
}

internal sealed class AdminResumeOnlineBookingHandler(IAdminDataScope scope, OnlineBookingPauses pauses)
    : ICommandHandler<AdminResumeOnlineBookingCommand, Result<OnlineBookingStateResponse>>
{
    public async Task<Result<OnlineBookingStateResponse>> Handle(AdminResumeOnlineBookingCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await pauses.ResumeAsync(new ShopId(command.ShopId), byAdmin: true, cancellationToken);
    }
}
