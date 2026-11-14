using FluentValidation;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Identity.Application.Barbers;

namespace Trimme.Modules.Identity.Application.Admin;

// The admin's commands (D-145): any shop's barber, through the admin data scope.

internal sealed record GetAdminBarberLoginQuery(Guid ProfessionalId) : IQuery<Result<BarberLoginResponse>>;

internal sealed record EnableAdminBarberLoginCommand(Guid ProfessionalId, string Password) : ICommand<Result<BarberLoginResponse>>;

internal sealed record DisableAdminBarberLoginCommand(Guid ProfessionalId) : ICommand<Result<BarberLoginResponse>>;

internal sealed class EnableAdminBarberLoginValidator : AbstractValidator<EnableAdminBarberLoginCommand>
{
    public EnableAdminBarberLoginValidator() =>
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required).MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
}

internal sealed class GetAdminBarberLoginHandler(BarberLoginManager manager, IAdminDataScope scope)
    : IQueryHandler<GetAdminBarberLoginQuery, Result<BarberLoginResponse>>
{
    public async Task<Result<BarberLoginResponse>> Handle(GetAdminBarberLoginQuery query, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await manager.GetAsync(query.ProfessionalId, cancellationToken);
    }
}

internal sealed class EnableAdminBarberLoginHandler(BarberLoginManager manager, IAdminDataScope scope)
    : ICommandHandler<EnableAdminBarberLoginCommand, Result<BarberLoginResponse>>
{
    public async Task<Result<BarberLoginResponse>> Handle(EnableAdminBarberLoginCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await manager.EnableAsync(command.ProfessionalId, command.Password, cancellationToken);
    }
}

internal sealed class DisableAdminBarberLoginHandler(BarberLoginManager manager, IAdminDataScope scope)
    : ICommandHandler<DisableAdminBarberLoginCommand, Result<BarberLoginResponse>>
{
    public async Task<Result<BarberLoginResponse>> Handle(DisableAdminBarberLoginCommand command, CancellationToken cancellationToken)
    {
        using var _ = scope.Begin();
        return await manager.DisableAsync(command.ProfessionalId, cancellationToken);
    }
}
