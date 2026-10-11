using System.Linq.Expressions;

namespace Deutchezz.Application.Common;

public static class QueryableSorting
{
    /// <summary>
    /// Applies an optional client-selected sort over a whitelist of field names, falling back to the
    /// supplied default ordering. Unknown field names use the default; direction is asc unless the
    /// query asked for <c>desc</c>.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        string? sort,
        string? direction,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> sortable,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultSort)
    {
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(sort) && sortable.TryGetValue(sort.Trim(), out var selector))
        {
            return descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
        }

        return defaultSort(query);
    }

    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        QueryParameters parameters,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> sortable,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultSort)
        => query.ApplySort(parameters.Sort, parameters.Direction, sortable, defaultSort);
}