using System.Globalization;
using SmartFactory.Core.Periods;
using SmartFactory.Performance.Rules;

namespace SmartFactory.Web.Infrastructure;

/// <summary>Formatting rules from the design system: dd Mmm yyyy, tabular numbers, statuses spelled Met / — / Unmet.</summary>
public static class Fmt
{
    public static string Date(DateOnly d) => FiscalCalendar.FormatDate(d);
    public static string Date(DateTime d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string Range(DateOnly a, DateOnly b) => FiscalCalendar.FormatRange(a, b);
    public static string Num(decimal? v, int decimals = 2) => v is null ? "—" : v.Value.ToString("N" + decimals, CultureInfo.InvariantCulture);
    public static string Plain(decimal? v) => v is null ? "" : v.Value.ToString("0.######", CultureInfo.InvariantCulture);
    public static string Pct(decimal? v, int decimals = 1) => v is null ? "—" : (v.Value * 100).ToString("N" + decimals, CultureInfo.InvariantCulture) + "%";
    public static string Rating(int? r) => r?.ToString() ?? "—";
    public static string Status(MonthStatus s) => s switch { MonthStatus.Met => "Met", MonthStatus.Unmet => "Unmet", MonthStatus.Open => "—", _ => "No status" };
    public static string StatusKey(MonthStatus s) => s switch { MonthStatus.Met => "met", MonthStatus.Unmet => "unmet", MonthStatus.Open => "open", _ => "none" };
    public static string Judgement(Judgement? j) => j switch { Rules_Met => "Met", Rules_Unmet => "Unmet", _ => "—" };
    private const Judgement Rules_Met = SmartFactory.Performance.Rules.Judgement.Met;
    private const Judgement Rules_Unmet = SmartFactory.Performance.Rules.Judgement.Unmet;
    public static string JudgementKey(Judgement? j) => j switch { Rules_Met => "met", Rules_Unmet => "unmet", _ => "none" };
    public static string Month(int monthIndex, FiscalHalf half) => half.Months.First(m => m.Index == monthIndex).Label[..3];
}

/// <summary>SVG text helper: Razor reserves the &lt;text&gt; tag, so SVG labels are emitted through this.</summary>
public static class Svg
{
    public static Microsoft.AspNetCore.Html.HtmlString Text(double x, double y, string content, string fill = "var(--sf-ink-muted)", int size = 11, string anchor = "start", string family = "var(--font-sans)", string weight = "400", string? extra = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var html = $"<text x=\"{x.ToString("0.#", inv)}\" y=\"{y.ToString("0.#", inv)}\" text-anchor=\"{anchor}\" fill=\"{fill}\" font-size=\"{size}\" font-family=\"{family}\" font-weight=\"{weight}\" {extra}>{System.Net.WebUtility.HtmlEncode(content)}</text>";
        return new Microsoft.AspNetCore.Html.HtmlString(html);
    }
}
