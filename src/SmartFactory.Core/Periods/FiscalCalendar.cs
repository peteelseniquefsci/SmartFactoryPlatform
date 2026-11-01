using System.Globalization;

namespace SmartFactory.Core.Periods;

/// <summary>
/// Rule 5.1. Work weeks are ISO weeks (Monday–Sunday) labelled WWnn by ISO week number.
/// A week belongs to the month of its Thursday. The calendar is generated, never typed.
/// </summary>
public static class FiscalCalendar
{
    public static string HalfLabel(int fiscalYear, int half) => $"{half}H FY{fiscalYear}";

    /// <summary>The first calendar month of a half: April for 1H, October for 2H.</summary>
    public static (int year, int month) FirstMonth(int fiscalYear, int half)
        => half == 1 ? (fiscalYear, 4) : (fiscalYear, 10);

    public static DateOnly StartOfHalf(int fiscalYear, int half)
    {
        var (y, m) = FirstMonth(fiscalYear, half);
        return new DateOnly(y, m, 1);
    }

    public static DateOnly EndOfHalf(int fiscalYear, int half)
        => StartOfHalf(fiscalYear, half).AddMonths(6).AddDays(-1);

    /// <summary>Fiscal year and half for a calendar date.</summary>
    public static (int fiscalYear, int half) HalfOf(DateOnly date)
        => date.Month >= 4 && date.Month <= 9 ? (date.Year, 1) : (date.Month >= 10 ? date.Year : date.Year - 1, 2);

    public static DateOnly MondayOf(DateOnly date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.AddDays(-diff);
    }

    /// <summary>Builds the half with its six months and their work weeks.</summary>
    public static FiscalHalf Build(int fiscalYear, int half)
    {
        var fh = new FiscalHalf
        {
            FiscalYear = fiscalYear,
            Half = half,
            Label = HalfLabel(fiscalYear, half),
            StartDate = StartOfHalf(fiscalYear, half),
            EndDate = EndOfHalf(fiscalYear, half),
        };

        for (int i = 0; i < 6; i++)
        {
            var first = fh.StartDate.AddMonths(i);
            fh.Months.Add(new FiscalMonth
            {
                Index = i + 1,
                Year = first.Year,
                Month = first.Month,
                Label = first.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                FiscalHalf = fh,
            });
        }

        // Walk Mondays from the week holding the first day of the half until the week's Thursday leaves the half.
        var monday = MondayOf(fh.StartDate);
        int sequence = 0;
        while (true)
        {
            var thursday = monday.AddDays(3);
            if (thursday > fh.EndDate) break;
            var month = fh.Months.FirstOrDefault(m => m.Year == thursday.Year && m.Month == thursday.Month);
            if (month is not null)
            {
                sequence++;
                var ww = new WorkWeek
                {
                    FiscalMonth = month,
                    IsoYear = ISOWeek.GetYear(thursday.ToDateTime(TimeOnly.MinValue)),
                    IsoWeek = ISOWeek.GetWeekOfYear(thursday.ToDateTime(TimeOnly.MinValue)),
                    StartDate = monday,
                    EndDate = monday.AddDays(6),
                    Sequence = sequence,
                };
                ww.Label = $"WW{ww.IsoWeek:00}";
                month.Weeks.Add(ww);
            }
            monday = monday.AddDays(7);
        }

        foreach (var m in fh.Months)
        {
            m.WeeksInMonth = m.Weeks.Count;
            for (int k = 0; k < m.Weeks.Count; k++)
            {
                m.Weeks[k].K = k + 1;
                m.Weeks[k].N = m.Weeks.Count;
            }
        }
        return fh;
    }

    public static IEnumerable<WorkWeek> AllWeeks(FiscalHalf half) => half.Months.SelectMany(m => m.Weeks).OrderBy(w => w.Sequence);

    /// <summary>The work week that holds a date, or null when the date is outside the half.</summary>
    public static WorkWeek? WeekOf(FiscalHalf half, DateOnly date) => AllWeeks(half).FirstOrDefault(w => date >= w.StartDate && date <= w.EndDate);

    public static string FormatDate(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string FormatRange(DateOnly a, DateOnly b)
    {
        if (a.Year == b.Year && a.Month == b.Month) return $"{a.Day}–{b.Day} {b.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        if (a.Year == b.Year) return $"{a.ToString("d MMM", CultureInfo.InvariantCulture)} – {b.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        return $"{FormatDate(a)} – {FormatDate(b)}";
    }
}
