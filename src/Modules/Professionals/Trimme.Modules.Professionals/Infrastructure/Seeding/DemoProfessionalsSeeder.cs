using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trimme.BuildingBlocks.Application.Privacy;
using Trimme.BuildingBlocks.Application.Tenancy;
using Trimme.BuildingBlocks.Domain.Privacy;
using Trimme.BuildingBlocks.Infrastructure.Persistence;
using Trimme.BuildingBlocks.Web.Hosting;
using Trimme.Modules.Professionals.Domain;

namespace Trimme.Modules.Professionals.Infrastructure.Seeding;

/// <summary>Separate professionals for each demo shop with fake WhatsApp numbers (idempotent; development only).</summary>
internal sealed class DemoProfessionalsSeeder : IDevSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>After the demo shops (200).</summary>
    public int Order => 250;

    public string Name => "professionals-demo";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrimmeDbContext>();
        var protector = services.GetRequiredService<IPersonalDataProtector>();
        using var scope = services.GetRequiredService<ISystemDataScope>().Begin();

        foreach (var demo in DemoData.Professionals)
        {
            var id = new ProfessionalId(demo.Id);
            if (await db.Set<Professional>().AnyAsync(p => p.Id == id, cancellationToken))
            {
                continue;
            }

            var professional = Professional.Create(
                id,
                demo.ShopId,
                demo.Slug,
                ProfessionalProfile.Create(demo.NameAr, demo.NameEn, demo.SpecialtyAr, demo.SpecialtyEn, null, null),
                SeededAt).Value;
            var phone = PhoneNumber.TryParseMobile(demo.WhatsApp, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Demo number for {demo.Slug} is not a valid mobile.");
            var contact = ProfessionalContact.For(professional);
            contact.Set(
                new ProtectedPhone(
                    protector.Protect(phone.E164, PersonalDataPurposes.ProfessionalWhatsApp),
                    protector.LookupHash(phone.E164, PersonalDataPurposes.ProfessionalWhatsApp),
                    phone.Masked),
                notificationsEnabled: true,
                SeededAt);
            db.Add(professional);
            db.Add(contact);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
