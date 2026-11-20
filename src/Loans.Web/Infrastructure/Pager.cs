namespace Loans.Web.Infrastructure;

/// <summary>Paging links that preserve the current query-string filters.</summary>
public sealed record Pager(string PagePath, int Page, int PageSize, int TotalCount, IReadOnlyDictionary<string, string?> Filters)
{
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

    public int FirstItem => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(Page * PageSize, TotalCount);

    public Dictionary<string, string> RouteFor(int page)
    {
        var values = Filters
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value!);
        values["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }
}
