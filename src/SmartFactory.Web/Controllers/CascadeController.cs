using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Organization;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

/// <summary>The orbit: company → Digital Systems / E&amp;A → sections → people. Everyone may see the shape; slot details follow permissions.</summary>
[Route("cascade")]
public class CascadeController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly EvaluationService _eval;
    private readonly OrganizationService _org;
    private readonly SmartFactoryDbContext _db;

    public CascadeController(WebCurrentUserAccessor current, PeriodService periods, EvaluationService eval, OrganizationService org, SmartFactoryDbContext db)
    { _current = current; _periods = periods; _eval = eval; _org = org; _db = db; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? node)
    {
        var user = (await _current.LoadAsync())!;
        var half = await _periods.CurrentHalfAsync();
        var week = _periods.ThisWeek(half);
        var ctx = EvaluationContext.WeeklyAt(half, week);
        var positions = await _org.PositionsAsync();
        var people = await _db.People.Include(p => p.Position)!.ThenInclude(p => p!.Section).Where(p => p.Active).OrderBy(p => p.Position!.SortOrder).ThenBy(p => p.Name).ToListAsync();
        var visible = (await _org.VisiblePeopleAsync(user)).Select(p => p.Id).ToHashSet();
        var evals = (await _eval.ForPeopleAsync(visible, ctx)).ToDictionary(e => e.Kra.PersonId);

        var all = new List<CascadeNode>();
        CascadeNode Node(Person p, int ring, string? parent)
        {
            var n = new CascadeNode { Key = $"p{p.Id}", Title = p.Name, Subtitle = p.Position?.Title ?? "", Ring = ring, ParentKey = parent, Person = p, Evaluation = evals.GetValueOrDefault(p.Id) };
            all.Add(n); return n;
        }
        var top = positions.First(p => p.ReportsToPositionId is null);
        var topPerson = people.FirstOrDefault(p => p.PositionId == top.Id);
        var root = topPerson is not null ? Node(topPerson, 0, null) : new CascadeNode { Key = "company", Title = top.Title, Subtitle = "vacant", Ring = 0 };
        if (topPerson is null) all.Add(root);

        void AddChildren(CascadeNode parent, int positionId, int ring)
        {
            foreach (var pos in positions.Where(p => p.ReportsToPositionId == positionId))
            {
                var holders = people.Where(p => p.PositionId == pos.Id).ToList();
                if (holders.Count == 0)
                {
                    var vacant = new CascadeNode { Key = $"v{pos.Id}", Title = pos.Title, Subtitle = $"vacant · {pos.Section?.Name}", Ring = ring, ParentKey = parent.Key };
                    all.Add(vacant); parent.Children.Add(vacant);
                    AddChildren(vacant, pos.Id, ring + 1);
                    continue;
                }
                foreach (var h in holders)
                {
                    var n = Node(h, ring, parent.Key);
                    parent.Children.Add(n);
                    AddChildren(n, pos.Id, ring + 1);
                }
            }
        }
        AddChildren(root, top.Id, 1);

        var selected = all.FirstOrDefault(n => n.Key == node) ?? root;
        return View(new CascadeViewModel { User = user, Half = half, Week = week, Root = root, All = all, Selected = selected });
    }
}
