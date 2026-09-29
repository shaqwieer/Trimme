namespace Trimme.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Marks an entity whose changes alter what public pages show (shop profile, catalogue, professionals, hours, reviews,
/// discovery visibility). A successful save that adds, changes or deletes one evicts the API's public response cache
/// (D-093).
/// </summary>
public interface IPublicContent;
