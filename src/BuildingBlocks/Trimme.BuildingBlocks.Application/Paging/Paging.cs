namespace Trimme.BuildingBlocks.Application.Paging;

/// <summary>A page request with safe bounds (spec §18, §21; R-FND-13): page ≥ 1, 1 ≤ size ≤ <see cref="MaxPageSize"/>.</summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int? page, int? pageSize)
    {
        Page = Math.Max(1, page ?? 1);
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>The paged envelope every list endpoint returns.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
