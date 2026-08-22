namespace NEM.Web.Services;

/// <summary>
/// URLs into the documentation site published from <c>docs/</c>. Kept in one place so a reader of
/// the dashboard always lands on a page that exists, and so the site's base URL is never repeated.
/// </summary>
public static class DocumentationLinks
{
    private const string BaseUrl = "https://nemsim-docs.pages.dev";

    /// <summary>The documentation site's home page.</summary>
    public const string Home = BaseUrl + "/";

    /// <summary>
    /// The limitations page: the four required limitations, ahead of the full assumptions register.
    /// The page a reader should reach before quoting any figure shown on this dashboard.
    /// </summary>
    public const string Limitations = BaseUrl + "/assumptions/limitations.html";
}
