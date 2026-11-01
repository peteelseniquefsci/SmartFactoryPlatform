using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Infrastructure.Data;

namespace SmartFactory.Infrastructure.Services;

/// <summary>Reporting-line questions: who is beneath whom, who may see whom.</summary>
public class OrganizationService
{
    private readonly SmartFactoryDbContext _db;
    public OrganizationService(SmartFactoryDbContext db) { _db = db; }

    public Task<List<Position>> PositionsAsync() => _db.Positions.Include(p => p.Section).ThenInclude(s => s!.ParentSection).OrderBy(p => p.SortOrder).ToListAsync();

    public Task<List<Section>> SectionsAsync() => _db.Sections.Include(s => s.ParentSection).OrderBy(s => s.SortOrder).ToListAsync();

    public async Task<Person?> LoadPersonAsync(int personId)
        => await _db.People.Include(p => p.Position)!.ThenInclude(p => p!.Section).Include(p => p.Roles).FirstOrDefaultAsync(p => p.Id == personId);

    public async Task<Person?> LoadPersonByEmployeeIdAsync(string employeeId)
        => await _db.People.Include(p => p.Position)!.ThenInclude(p => p!.Section).Include(p => p.Roles).FirstOrDefaultAsync(p => p.EmployeeId == employeeId);

    /// <summary>Positions beneath a person's position, through the reporting line.</summary>
    public async Task<HashSet<int>> SubordinatePositionIdsAsync(Person person)
    {
        var all = await PositionsAsync();
        return OrganizationTree.SubordinatePositions(all, person.PositionId);
    }

    /// <summary>People a user may see: themselves, plus everyone beneath them when they are a head or manager; everyone for administrators and HR viewers.</summary>
    public async Task<List<Person>> VisiblePeopleAsync(CurrentUser user, bool activeOnly = true)
    {
        var q = _db.People.Include(p => p.Position)!.ThenInclude(p => p!.Section).AsQueryable();
        if (activeOnly) q = q.Where(p => p.Active);
        if (user.IsAdministrator || user.Is(PlatformRole.HrViewer)) return await q.OrderBy(p => p.Position!.SortOrder).ThenBy(p => p.Name).ToListAsync();
        if (!user.IsHeadOrManager) return await q.Where(p => p.Id == user.PersonId).ToListAsync();
        var subs = await SubordinatePositionIdsAsync(user.Person);
        return await q.Where(p => p.Id == user.PersonId || subs.Contains(p.PositionId)).OrderBy(p => p.Position!.SortOrder).ThenBy(p => p.Name).ToListAsync();
    }

    public async Task<bool> CanViewAsync(CurrentUser user, Person target)
    {
        if (target.Id == user.PersonId || user.IsAdministrator || user.Is(PlatformRole.HrViewer)) return true;
        if (!user.IsHeadOrManager) return false;
        var subs = await SubordinatePositionIdsAsync(user.Person);
        return subs.Contains(target.PositionId);
    }

    /// <summary>The superior who approves a person's KRA: the first person holding the position the target reports to (walking up past vacancies).</summary>
    public async Task<List<Person>> ApproversOfAsync(Person target)
    {
        var all = await PositionsAsync();
        var pos = all.First(p => p.Id == target.PositionId);
        while (pos.ReportsToPositionId is int up)
        {
            var holders = await _db.People.Where(p => p.PositionId == up && p.Active).ToListAsync();
            if (holders.Count > 0) return holders;
            pos = all.First(p => p.Id == up);
        }
        return new List<Person>();
    }

    /// <summary>True when the user holds a position above the target in the reporting line (the person who approves).</summary>
    public async Task<bool> IsSuperiorOfAsync(CurrentUser user, Person target)
    {
        if (!user.IsHeadOrManager) return false;
        var subs = await SubordinatePositionIdsAsync(user.Person);
        return subs.Contains(target.PositionId);
    }
}
