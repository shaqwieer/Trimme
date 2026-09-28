using Trimme.BuildingBlocks.Application.Scheduling;
using Trimme.BuildingBlocks.Domain.Tenancy;

namespace Trimme.Modules.Availability.Infrastructure;

/// <summary>
/// Stand-in <see cref="IBookedTimeReader"/> until bookings exist (Phase 10): no time is taken. The Bookings module
/// replaces this registration (<c>services.Replace</c>) with the real reader.
/// </summary>
internal sealed class NoBookedTime : IBookedTimeReader
{
    public Task<IReadOnlyList<BusyTime>> GetBusyAsync(
        ShopId shopId, IReadOnlyCollection<ProfessionalId> professionalIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BusyTime>>([]);

    public Task<IReadOnlyList<BookedAppointment>> GetAppointmentsAsync(
        ShopId shopId, ProfessionalId? professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BookedAppointment>>([]);
}
