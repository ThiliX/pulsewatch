namespace PulseWatch.Api.Common;

/// <summary>Shared limits for paged list endpoints, so no query can load an unbounded result.</summary>
internal static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static void Validate(int page, int pageSize, IDictionary<string, string[]> errors)
    {
        if (page < 1)
        {
            errors["page"] = ["Page must be 1 or more."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }
    }

    public static IQueryable<T> Page<T>(this IQueryable<T> query, int page, int pageSize) =>
        query.Skip((page - 1) * pageSize).Take(pageSize);
}
