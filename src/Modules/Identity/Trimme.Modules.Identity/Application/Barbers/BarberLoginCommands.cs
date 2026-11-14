using FluentValidation;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Domain.Results;

namespace Trimme.Modules.Identity.Application.Barbers;

// The owner's commands (D-145): read through the shop's tenant, so only the owner's own barbers are found.

internal sealed record GetShopBarberLoginQuery(Guid ProfessionalId) : IQuery<Result<BarberLoginResponse>>;

internal sealed record EnableShopBarberLoginCommand(Guid ProfessionalId, string Password) : ICommand<Result<BarberLoginResponse>>;

internal sealed record DisableShopBarberLoginCommand(Guid ProfessionalId) : ICommand<Result<BarberLoginResponse>>;

internal sealed class EnableShopBarberLoginValidator : AbstractValidator<EnableShopBarberLoginCommand>
{
    public EnableShopBarberLoginValidator() =>
        RuleFor(c => c.Password).NotEmpty().WithErrorCode(ValidationCodes.Required).MaximumLength(256).WithErrorCode(ValidationCodes.TooLong);
}

internal sealed class GetShopBarberLoginHandler(BarberLoginManager manager) : IQueryHandler<GetShopBarberLoginQuery, Result<BarberLoginResponse>>
{
    public Task<Result<BarberLoginResponse>> Handle(GetShopBarberLoginQuery query, CancellationToken cancellationToken) =>
        manager.GetAsync(query.ProfessionalId, cancellationToken);
}

internal sealed class EnableShopBarberLoginHandler(BarberLoginManager manager) : ICommandHandler<EnableShopBarberLoginCommand, Result<BarberLoginResponse>>
{
    public Task<Result<BarberLoginResponse>> Handle(EnableShopBarberLoginCommand command, CancellationToken cancellationToken) =>
        manager.EnableAsync(command.ProfessionalId, command.Password, cancellationToken);
}

internal sealed class DisableShopBarberLoginHandler(BarberLoginManager manager) : ICommandHandler<DisableShopBarberLoginCommand, Result<BarberLoginResponse>>
{
    public Task<Result<BarberLoginResponse>> Handle(DisableShopBarberLoginCommand command, CancellationToken cancellationToken) =>
        manager.DisableAsync(command.ProfessionalId, cancellationToken);
}
