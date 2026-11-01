using System.IO.Compression;
using ClosedXML.Excel;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Performance.Export;

namespace SmartFactory.Infrastructure.Export;

/// <summary>Excel on the company Target Indices form: one file per person, plus one consolidated file (workflow 4).</summary>
public class ExcelExportService
{
    public byte[] PersonWorkbook(KraEvaluation eval, string departmentName)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, TargetIndicesLayout.Build(eval, departmentName), "Target Indices");
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ConsolidatedWorkbook(IEnumerable<KraEvaluation> evals, string departmentName)
    {
        using var wb = new XLWorkbook();
        var sheets = evals.Select(e => TargetIndicesLayout.Build(e, departmentName)).ToList();
        var summary = wb.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = $"Smart Factory – {sheets.FirstOrDefault()?.HalfLabel} – consolidated";
        summary.Cell(1, 1).Style.Font.Bold = true;
        var headers = new List<string> { "Employee id", "Name", "Position", "Section", "Mode", "Version" };
        if (sheets.Count > 0) headers.AddRange(sheets[0].MonthLabels);
        headers.Add("2H performance");
        for (int c = 0; c < headers.Count; c++) { summary.Cell(3, c + 1).Value = headers[c]; summary.Cell(3, c + 1).Style.Font.Bold = true; }
        int row = 4;
        foreach (var s in sheets)
        {
            summary.Cell(row, 1).Value = s.EmployeeId; summary.Cell(row, 2).Value = s.PersonName; summary.Cell(row, 3).Value = s.PositionTitle; summary.Cell(row, 4).Value = s.SectionName;
            summary.Cell(row, 5).Value = s.Mode; summary.Cell(row, 6).Value = s.Version;
            for (int m = 0; m < s.MonthlyTotals.Length; m++) { var v = s.MonthlyTotals[m]; if (v.HasValue) summary.Cell(row, 7 + m).Value = Math.Round(v.Value, 2); }
            if (s.HalfPerformance.HasValue) summary.Cell(row, 7 + s.MonthlyTotals.Length).Value = Math.Round(s.HalfPerformance.Value, 2);
            row++;
        }
        summary.Columns().AdjustToContents();
        var used = new HashSet<string>();
        foreach (var s in sheets)
        {
            var name = SheetName(s.PersonName, used);
            WriteSheet(wb, s, name);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ZipOfPersonFiles(IEnumerable<KraEvaluation> evals, string departmentName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var e in evals)
            {
                var entry = zip.CreateEntry(FileName(e));
                using var es = entry.Open();
                var bytes = PersonWorkbook(e, departmentName);
                es.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    public static string FileName(KraEvaluation e)
    {
        var safe = string.Concat((e.Kra.Person?.Name ?? "person").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return $"Target_Indices_{e.Context.Half.Label.Replace(' ', '_')}_{safe}.xlsx";
    }

    private static string SheetName(string name, HashSet<string> used)
    {
        var s = string.Concat(name.Where(ch => !"[]:*?/\\".Contains(ch)));
        if (s.Length > 28) s = s[..28];
        var candidate = s; int i = 2;
        while (!used.Add(candidate)) candidate = $"{s} {i++}";
        return candidate;
    }

    private static void WriteSheet(XLWorkbook wb, TargetIndicesSheet s, string sheetName)
    {
        var ws = wb.Worksheets.Add(sheetName);
        ws.Cell(1, 1).Value = "TARGET INDICES"; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Name"; ws.Cell(2, 2).Value = s.PersonName;
        ws.Cell(2, 4).Value = "Employee id"; ws.Cell(2, 5).Value = s.EmployeeId;
        ws.Cell(3, 1).Value = "Position"; ws.Cell(3, 2).Value = s.PositionTitle;
        ws.Cell(3, 4).Value = "Section"; ws.Cell(3, 5).Value = s.SectionName;
        ws.Cell(4, 1).Value = "Department"; ws.Cell(4, 2).Value = s.DepartmentName;
        ws.Cell(4, 4).Value = "Period"; ws.Cell(4, 5).Value = s.HalfLabel;
        ws.Cell(5, 1).Value = "KRA mode"; ws.Cell(5, 2).Value = $"{s.Mode} v{s.Version}";
        ws.Cell(5, 4).Value = "Approved by"; ws.Cell(5, 5).Value = s.ApprovedBy is null ? "" : $"{s.ApprovedBy}, {s.ApprovedAt:dd MMM yyyy}";

        int headerRow = 7;
        var headers = TargetIndicesLayout.Headers.ToList();
        headers.AddRange(s.MonthLabels.Select(m => $"Rating {m}"));
        headers.Add("2H rating"); headers.Add("Weighted score"); headers.Add("Rating reference");
        for (int c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c]; cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9D9D9"); cell.Style.Alignment.WrapText = true;
        }
        int r = headerRow + 1;
        int ratingCol = TargetIndicesLayout.Headers.Length + 1;
        foreach (var row in s.Rows)
        {
            ws.Cell(r, 1).Value = row.SlotNumber; ws.Cell(r, 2).Value = row.FocusItem; ws.Cell(r, 3).Value = row.StrategicObjective; ws.Cell(r, 4).Value = row.TopGoalsMeasure;
            ws.Cell(r, 5).Value = row.DepartmentalGoal; ws.Cell(r, 6).Value = row.DepartmentalMeasure; ws.Cell(r, 7).Value = row.SectionalGoal; ws.Cell(r, 8).Value = row.SectionalMeasure;
            ws.Cell(r, 9).Value = row.Measure; ws.Cell(r, 10).Value = row.Target; ws.Cell(r, 11).Value = row.Weight;
            for (int m = 0; m < row.MonthlyRatings.Length; m++)
            {
                var v = row.MonthlyRatings[m];
                if (v.HasValue) { ws.Cell(r, ratingCol + m).Value = v.Value; if (v.Value < 3) ws.Cell(r, ratingCol + m).Style.Font.FontColor = XLColor.Red; }
            }
            int c2 = ratingCol + row.MonthlyRatings.Length;
            if (row.HalfRating.HasValue) { ws.Cell(r, c2).Value = Math.Round(row.HalfRating.Value, 2); if (row.HalfRating.Value < 3) ws.Cell(r, c2).Style.Font.FontColor = XLColor.Red; }
            if (row.WeightedScore.HasValue) ws.Cell(r, c2 + 1).Value = Math.Round(row.WeightedScore.Value, 4);
            ws.Cell(r, c2 + 2).Value = row.RatingReference;
            r++;
        }
        ws.Cell(r, 9).Value = "TOTAL"; ws.Cell(r, 9).Style.Font.Bold = true;
        ws.Cell(r, 11).Value = s.Rows.Sum(x => x.Weight); ws.Cell(r, 11).Style.Font.Bold = true;
        for (int m = 0; m < s.MonthlyTotals.Length; m++) { var v = s.MonthlyTotals[m]; if (v.HasValue) { ws.Cell(r, ratingCol + m).Value = Math.Round(v.Value, 2); ws.Cell(r, ratingCol + m).Style.Font.Bold = true; } }
        if (s.HalfPerformance.HasValue) { ws.Cell(r, ratingCol + s.MonthlyTotals.Length + 1).Value = Math.Round(s.HalfPerformance.Value, 2); ws.Cell(r, ratingCol + s.MonthlyTotals.Length + 1).Style.Font.Bold = true; }
        ws.Columns(1, headers.Count).AdjustToContents(headerRow, r, 8, 45);
        ws.SheetView.FreezeRows(headerRow);
    }
}
