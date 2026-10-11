namespace Deutchezz.Application.Common;

/// <summary>
/// List envelope: { items, page, pageSize, totalCount, totalPages }.
/// </summary>
public record PaginatedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}