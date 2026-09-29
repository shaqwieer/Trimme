using Trimme.BuildingBlocks.Application.Auditing;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Paging;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Reporting;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Results;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Application.Admin;

// The customers directory (a-appointments side card, DV-A08, DV-S17, R-AD-06, D-105). Rows carry the name and booking
// figures, never the mobile; the detail shows it masked; the full number is revealed only through an explicit,
// permission-gated command with a reason, audited without the number.

public sealed record AdminCustomerListItem(
    Guid Id, string? DisplayName, DateTimeOffset RegisteredAt, bool IsDisabled, int Bookings, int Upcoming, DateTimeOffset? LastBookingAt, DateTimeOffset? NextBookingAt);

/// <summary>A customer's profile for support: <c>PhoneMasked</c> only (for example <c>+966 5•• ••• •12</c>) and their booking figures.</summary>
public sealed record AdminCustomerResponse(
    Guid Id,
    string? DisplayName,
    string PreferredLocale,
    DateTimeOffset RegisteredAt,
    bool IsDisabled,
    string? PhoneMasked,
    int Bookings,
    int Upcoming,
    int Completed,
    int Cancelled,
    int NoShows,
    DateTimeOffset? LastBookingAt,
    DateTimeOffset? NextBookingAt);

/// <summary>The customer's full mobile in E.164, returned once to an authorized admin (never cached, never logged).</summary>
public sealed record CustomerContactResponse(string Phone);

internal sealed record ListAdminCustomersQuery(string? Search, PageRequest Page) : IQuery<PagedResponse<AdminCustomerListItem>>;

internal sealed record GetAdminCustomerQuery(Guid CustomerId) : IQuery<AdminCustomerResponse?>;

internal sealed record RevealCustomerContactCommand(Guid CustomerId, string? Reason) : ICommand<Result<CustomerContactResponse>>;

internal sealed class ListAdminCustomersHandler(IAdminAccounts accounts, IAdminDataScope scope, IBookingStatistics bookings, TimeProvider clock)
    : IQueryHandler<ListAdminCustomersQuery, PagedResponse<AdminCustomerListItem>>
{
    public async Task<PagedResponse<AdminCustomerListItem>> Handle(ListAdminCustomersQuery query, CancellationToken cancellationToken)
    {
        var (rows, total) = await accounts.SearchCustomersAsync(query.Search, query.Page, cancellationToken);
        IReadOnlyDictionary<Guid, CustomerBookingStats> stats;
        using (scope.Begin())
        {
            stats = await bookings.ByCustomerAsync([.. rows.Select(r => r.Id)], clock.GetUtcNow(), cancellationToken);
        }

        return new PagedResponse<AdminCustomerListItem>(
            [
                .. rows.Select(r =>
                {
                    var s = stats.GetValueOrDefault(r.Id) ?? CustomerBookingStats.Empty;
                    return new AdminCustomerListItem(r.Id, r.DisplayName, r.CreatedAt, r.IsDisabled, s.Bookings, s.Upcoming, s.LastBookingAt, s.NextBookingAt);
                }),
            ],
            query.Page.Page,
            query.Page.PageSize,
            total);
    }
}

internal sealed class GetAdminCustomerHandler(IAdminAccounts accounts, IAdminDataScope scope, IBookingStatistics bookings, IPersonalDataProtector protector, TimeProvider clock)
    : IQueryHandler<GetAdminCustomerQuery, AdminCustomerResponse?>
{
    public async Task<AdminCustomerResponse?> Handle(GetAdminCustomerQuery query, CancellationToken cancellationToken)
    {
        if (await accounts.FindCustomerAsync(query.CustomerId, cancellationToken) is not { } customer)
        {
            return null;
        }

        CustomerBookingStats stats;
        using (scope.Begin())
        {
            stats = (await bookings.ByCustomerAsync([customer.Id], clock.GetUtcNow(), cancellationToken)).GetValueOrDefault(customer.Id) ?? CustomerBookingStats.Empty;
        }

        var masked = customer.ProtectedPhone is null ? null : MobileNumber.Mask(protector.Unprotect(customer.ProtectedPhone, PersonalDataPurposes.MobileNumber));
        return new AdminCustomerResponse(
            customer.Id, customer.DisplayName, customer.PreferredLocale, customer.CreatedAt, customer.IsDisabled, masked, stats.Bookings, stats.Upcoming,
            stats.Completed, stats.Cancelled, stats.NoShows, stats.LastBookingAt, stats.NextBookingAt);
    }
}

/// <summary>"إظهار الرقم" (DV-S17): <c>Admin.Customers.ViewContact</c>, a reason of at least five characters, audited without the number.</summary>
internal sealed class RevealCustomerContactHandler(IAdminAccounts accounts, IPersonalDataProtector protector, IAuditLog audit, TrimmeDbContext db)
    : ICommandHandler<RevealCustomerContactCommand, Result<CustomerContactResponse>>
{
    public const int MinReasonLength = 5;
    public const int MaxReasonLength = 300;

    public async Task<Result<CustomerContactResponse>> Handle(RevealCustomerContactCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < MinReasonLength or > MaxReasonLength)
        {
            return Error.Validation("validation.failed", "A reason is required.",
                new Dictionary<string, string[]> { ["reason"] = [reason.Length < MinReasonLength ? "validation.reason_required" : ValidationCodes.TooLong] });
        }

        if (await accounts.FindCustomerAsync(command.CustomerId, cancellationToken) is not { ProtectedPhone: { } protectedPhone } customer)
        {
            return IdentityErrors.UserNotFound();
        }

        audit.Record(new AuditRecord("customer.contact_revealed", "Customer", customer.Id.ToString(), null, "Mobile number viewed", reason));
        await db.SaveChangesAsync(cancellationToken);
        return new CustomerContactResponse(protector.Unprotect(protectedPhone, PersonalDataPurposes.MobileNumber));
    }
}
