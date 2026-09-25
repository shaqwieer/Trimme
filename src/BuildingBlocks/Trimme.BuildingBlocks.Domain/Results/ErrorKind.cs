namespace Trimme.BuildingBlocks.Domain.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    BusinessRule,
    RateLimited,
    Unexpected,
}
