namespace Loans.Core.Data.Queries;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

internal static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));

    /// <summary>Case-insensitive (ASCII, SQLite LIKE) contains pattern with LIKE wildcards escaped.</summary>
    public static string ContainsPattern(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public const string LikeEscape = "\\";
}
