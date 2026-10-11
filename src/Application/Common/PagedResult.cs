namespace Deutchezz.Application.Common;

/// <summary>
/// List envelope: { items, page, pageSize, totalCount, totalPages }.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}