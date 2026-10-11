namespace Deutchezz.Application.Common;

/// <summary>
/// Pagination/filtering base for operational list endpoints. Query-string naming is camelCase
/// per the SoT contract conventions: page (default 1), pageSize (default 20, max 100),
/// plus optional sort (field name) and direction (asc|desc).
/// </summary>
public abstract class QueryParameters
{
    private int _page = 1;
    private int _pageSize = 20;
    private string? _direction;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }

    /// <summary>Field name to sort by; unknown/blank values fall back to the endpoint default.</summary>
    public string? Sort { get; set; }

    /// <summary>Sort direction: <c>asc</c> (default) or <c>desc</c>; any other value falls back to asc.</summary>
    public string? Direction
    {
        get => _direction;
        set => _direction = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    public bool IsDescending => _direction == "desc";
}