using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Export;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;

namespace SmartFactory.Web.Controllers;

[Authorize(Policy = Policies.Exports)]
[Route("exports")]
public class ExportsController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly EvaluationService _eval;
    private readonly ExcelExportService _excel;
    private readonly SmartFactoryDbContext _db;

    public ExportsController(WebCurrentUserAccessor current, PeriodService periods, EvaluationService eval, ExcelExportService excel, SmartFactoryDbContext db)
    { _current = current; _periods = periods; _eval = eval; _excel = excel; _db = db; }

    private async Task<string> DepartmentAsync() => (await _db.Departments.FirstOrDefaultAsync())?.Name ?? "Smart Factory";

    [HttpGet("")]
    public async Task<IActionResult> Index(int? half)
    {
        var user = (await _current.LoadAsync())!;
        var h = half is null ? await _periods.CurrentHalfAsync() : await _periods.HalfAsync(half.Value);
        if (h is null) return NotFound();
        ViewBag.Half = h; ViewBag.Halves = await _periods.HalvesAsync();
        ViewBag.Evals = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.Monthly(h));
        ViewBag.Today = _periods.Today;
        return View();
    }

    [HttpGet("person/{id:int}")]
    public async Task<IActionResult> Person(int id, int? half)
    {
        var user = (await _current.LoadAsync())!;
        var h = half is null ? await _periods.CurrentHalfAsync() : await _periods.HalfAsync(half.Value);
        if (h is null) return NotFound();
        var evals = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.Monthly(h));
        var e = evals.FirstOrDefault(x => x.Kra.PersonId == id);
        if (e is null) return Forbid();
        return File(_excel.PersonWorkbook(e, await DepartmentAsync()), Xlsx, ExcelExportService.FileName(e));
    }

    [HttpGet("consolidated")]
    public async Task<IActionResult> Consolidated(int? half)
    {
        var user = (await _current.LoadAsync())!;
        var h = half is null ? await _periods.CurrentHalfAsync() : await _periods.HalfAsync(half.Value);
        if (h is null) return NotFound();
        var evals = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.Monthly(h));
        return File(_excel.ConsolidatedWorkbook(evals, await DepartmentAsync()), Xlsx, $"Target_Indices_{h.Label.Replace(' ', '_')}_consolidated.xlsx");
    }

    [HttpGet("all")]
    public async Task<IActionResult> All(int? half)
    {
        var user = (await _current.LoadAsync())!;
        var h = half is null ? await _periods.CurrentHalfAsync() : await _periods.HalfAsync(half.Value);
        if (h is null) return NotFound();
        var evals = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.Monthly(h));
        return File(_excel.ZipOfPersonFiles(evals, await DepartmentAsync()), "application/zip", $"Target_Indices_{h.Label.Replace(' ', '_')}_per_person.zip");
    }
}
