namespace Deutchezz.Application.Common;

/// <summary>
/// Pagination/filtering convention for list endpoints. Query-string naming is camelCase
/// (page, pageSize, sort, direction). Defaults: page 1, pageSize 20 (max 100).
/// </summary>
public class PagedRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }

    public string? Sort { get; set; }
    public string? Direction { get; set; }

    public bool IsDescending => string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase);

    public int Offset => (Page - 1) * PageSize;
}