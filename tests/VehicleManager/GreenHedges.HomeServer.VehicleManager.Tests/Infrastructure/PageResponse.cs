using System.Net;
using System.Text.RegularExpressions;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

public sealed record PageResponse(HttpStatusCode Status, string Html, string? Location)
{
    /// <summary>The page's text content, with HTML entities decoded (Razor encodes e.g. "+" as &amp;#x2B;).</summary>
    public string Text => WebUtility.HtmlDecode(Html);

    /// <summary>Validation and error messages shown on the page.</summary>
    public string Errors => string.Join(" | ", Regex
        .Matches(Html, "class=\"(?:text-danger|validation-summary-errors|field-validation-error)[^\"]*\"[^>]*>(.*?)</", RegexOptions.Singleline)
        .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim())
        .Where(s => s.Length > 0));

    /// <summary>The href of the first link with this title (e.g. "Edit") in the table row whose first cell is <paramref name="rowName"/>.</summary>
    public string? RowLink(string rowName, string title)
    {
        var row = Regex.Match(Html, $@"<tr>\s*<td>{Regex.Escape(WebUtility.HtmlEncode(rowName))}</td>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value;
        return Href(Regex.Match(row, $"<a [^>]*title=\"{Regex.Escape(title)}[^\"]*\"[^>]*>").Value);
    }

    /// <summary>The value of the option with this text in a select.</summary>
    public string OptionValue(string text) =>
        Regex.Match(Html, $"<option[^>]*value=\"([^\"]*)\"[^>]*>{Regex.Escape(WebUtility.HtmlEncode(text))}</option>").Groups[1].Value;

    public static string? Href(string tag) =>
        Regex.Match(tag, "href=\"([^\"]+)\"") is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
}
