using System.Net;
using System.Text.RegularExpressions;

namespace VehicleManager.Tests.Infrastructure;

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

/// <summary>Loads pages and submits their forms, like a browser would.</summary>
public sealed class PageClient(HttpClient client)
{
    public async Task<PageResponse> GetAsync(string url) => await ReadAsync(await client.GetAsync(url));

    /// <summary>
    /// Submits the form on <paramref name="page"/> to <paramref name="url"/>: its current values
    /// (inputs, including the anti-forgery token and hidden fields, selected options and text areas),
    /// with <paramref name="fields"/> changed or added.
    /// </summary>
    public async Task<PageResponse> PostFormAsync(string url, PageResponse page, Dictionary<string, string>? fields = null)
    {
        var data = new Dictionary<string, string>();
        void Add(string name, string value) => data.TryAdd(name, WebUtility.HtmlDecode(value));

        // Attributes can come in any order (an explicit value="" is written before the tag helper's name="").
        foreach (Match m in Regex.Matches(page.Html, "<input [^>]*>"))
            if (Attribute(m.Value, "name") is { } name && Attribute(m.Value, "value") is { } value)
                Add(name, value);
        foreach (Match m in Regex.Matches(page.Html, "<select[^>]*name=\"([^\"]+)\"[^>]*>(.*?)</select>", RegexOptions.Singleline))
            if (Regex.Match(m.Groups[2].Value, "<option[^>]*selected[^>]*>") is { Success: true } option)
                Add(m.Groups[1].Value, Regex.Match(option.Value, "value=\"([^\"]*)\"").Groups[1].Value);
        foreach (Match m in Regex.Matches(page.Html, "<textarea[^>]*name=\"([^\"]+)\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline))
            Add(m.Groups[1].Value, m.Groups[2].Value.TrimStart('\r', '\n'));

        foreach (var (name, value) in fields ?? []) data[name] = value;
        return await ReadAsync(await client.PostAsync(url, new FormUrlEncodedContent(data)));
    }

    private static string? Attribute(string tag, string name) =>
        Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : null;

    private static async Task<PageResponse> ReadAsync(HttpResponseMessage response) =>
        new(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Location?.OriginalString);
}
