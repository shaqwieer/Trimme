using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Security;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Application;

/// <summary>A barber's names after they renamed themselves, with the version for the next change.</summary>
public sealed record BarberAccountNameResponse(Guid ProfessionalId, string NameAr, string NameEn, uint Version);

/// <summary><c>NameEn</c> left empty keeps the English name.</summary>
internal sealed record RenameOwnBarberCommand(string NameAr, string? NameEn, uint Version) : ICommand<Result<BarberAccountNameResponse>>;

/// <summary>
/// A barber renames themselves from «حسابي» (D-151, the user's answer: the name everywhere — their page, the owner's list
/// and new log lines). It is the owner's edit (<see cref="UpdateShopBarberCommand"/>: the same validation, audit,
/// optimistic concurrency and login name), with every other field of the profile kept. Only a barber's login, and only
/// their own professional; the owner's account has no barber name (403).
/// </summary>
internal sealed class RenameOwnBarberHandler(ICurrentUser user, IDispatcher dispatcher)
    : ICommandHandler<RenameOwnBarberCommand, Result<BarberAccountNameResponse>>
{
    public async Task<Result<BarberAccountNameResponse>> Handle(RenameOwnBarberCommand command, CancellationToken cancellationToken)
    {
        if (user.ProfessionalId is not { } own)
        {
            return Error.Forbidden("account.barbers_only", "Only a barber's login can do this; the shop's owner account is managed by the platform.");
        }

        if (await dispatcher.Send(new GetShopBarberQuery(own), cancellationToken) is not { } current)
        {
            return ProfessionalErrors.NotFound();
        }

        var updated = await dispatcher.Send(
            new UpdateShopBarberCommand(
                own,
                command.NameAr?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(command.NameEn) ? current.NameEn : command.NameEn.Trim(),
                current.SpecialtyAr,
                current.SpecialtyEn,
                current.BioAr,
                current.BioEn,
                command.Version,
                current.Languages),
            cancellationToken);
        return updated.IsFailure
            ? updated.Error
            : new BarberAccountNameResponse(updated.Value.Id, updated.Value.NameAr, updated.Value.NameEn, updated.Value.Version);
    }
}
