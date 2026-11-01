using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Definitions;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Performance.Rules;

namespace SmartFactory.Infrastructure.Services;

public class RuleViolationException : Exception { public RuleViolationException(string message) : base(message) { } }

/// <summary>One slot as edited on My KRA. Null fields on an inactive slot are allowed.</summary>
public class SlotInput
{
    public int SlotNumber { get; set; }
    public int? TopGoalLineId { get; set; }
    public string? DepartmentalGoal { get; set; }
    public string? DepartmentalMeasure { get; set; }
    public string? SectionalGoal { get; set; }
    public string? SectionalMeasure { get; set; }
    public string? Measure { get; set; }
    public string? TargetText { get; set; }
    public decimal Weight { get; set; }
    public int? RatingBasisId { get; set; }
    public Aggregation Aggregation { get; set; }
    public WeeklyRule WeeklyRule { get; set; }
    public decimal? StartValue { get; set; }
    public decimal?[] Plans { get; set; } = new decimal?[6];
    public string? Notes { get; set; }
    public bool Retired { get; set; }
}

/// <summary>KRA setup, submission, approval, lock and unlock (section 5.8 and workflows 1, 5, 6).</summary>
public class KraService
{
    private readonly SmartFactoryDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly OrganizationService _org;
    private readonly NotificationService _notify;

    public KraService(SmartFactoryDbContext db, IAuditTrail audit, IClock clock, OrganizationService org, NotificationService notify)
    { _db = db; _audit = audit; _clock = clock; _org = org; _notify = notify; }

    public IQueryable<Kra> FullKras() => _db.Kras
        .Include(k => k.Person)!.ThenInclude(p => p!.Position)!.ThenInclude(p => p!.Section)
        .Include(k => k.Slots.OrderBy(s => s.SlotNumber)).ThenInclude(s => s.TopGoalLine)
        .Include(k => k.Slots).ThenInclude(s => s.RatingBasis)
        .Include(k => k.Slots).ThenInclude(s => s.WeeklyResults).ThenInclude(r => r.WorkWeek)
        .Include(k => k.Slots).ThenInclude(s => s.MonthlyManualValues).ThenInclude(v => v.FiscalMonth)
        .Include(k => k.Slots).ThenInclude(s => s.Remarks).ThenInclude(r => r.Author)
        .Include(k => k.Versions)
        .AsSplitQuery();

    public Task<Kra?> LoadAsync(int kraId) => FullKras().FirstOrDefaultAsync(k => k.Id == kraId);

    public Task<Kra?> FindAsync(int personId, int halfId) => FullKras().FirstOrDefaultAsync(k => k.PersonId == personId && k.FiscalHalfId == halfId);

    /// <summary>The person's KRA for the half; a new incumbent inherits the position's template slots as a SETUP draft (workflow 6).</summary>
    public async Task<Kra> GetOrCreateAsync(Person person, FiscalHalf half)
    {
        var kra = await FindAsync(person.Id, half.Id);
        if (kra is not null) return kra;

        kra = new Kra { PersonId = person.Id, FiscalHalfId = half.Id, CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow };
        var template = await _db.PositionTemplateSlots.Where(t => t.PositionId == person.PositionId).OrderBy(t => t.SlotNumber).ToListAsync();
        var used = new HashSet<int>();
        foreach (var t in template)
        {
            used.Add(t.SlotNumber);
            kra.Slots.Add(new KraSlot
            {
                SlotNumber = t.SlotNumber, TopGoalLineId = t.TopGoalLineId, DepartmentalGoal = t.DepartmentalGoal, DepartmentalMeasure = t.DepartmentalMeasure,
                SectionalGoal = t.SectionalGoal, SectionalMeasure = t.SectionalMeasure, Measure = t.Measure, TargetText = t.TargetText, Weight = t.Weight,
                RatingBasisId = t.RatingBasisId, Aggregation = t.Aggregation, WeeklyRule = t.WeeklyRule, StartValue = t.StartValue,
                Plan1 = t.Plan1, Plan2 = t.Plan2, Plan3 = t.Plan3, Plan4 = t.Plan4, Plan5 = t.Plan5, Plan6 = t.Plan6, Notes = t.Notes,
            });
        }
        for (int n = 1; n <= 20; n++) if (!used.Contains(n)) kra.Slots.Add(new KraSlot { SlotNumber = n });
        _db.Kras.Add(kra);
        await _db.SaveChangesAsync();
        _audit.Record("Kra", kra.Id.ToString(), "Create", detail: template.Count > 0 ? $"Draft from the {person.Position?.Title} template ({template.Count} slots)" : "Empty draft");
        await _db.SaveChangesAsync();
        return (await FindAsync(person.Id, half.Id))!;
    }

    public static (decimal total, bool ok) WeightCheck(Kra kra) => RuleEngine.WeightCheck(kra.Slots.Where(s => s.IsActive).Select(s => s.Weight));

    /// <summary>Edit definitions. Allowed only in SETUP; each changed field is audited with old and new value.</summary>
    public async Task SaveSlotsAsync(Kra kra, IEnumerable<SlotInput> inputs, CurrentUser actor)
    {
        if (kra.Mode == DefinitionMode.Locked) throw new RuleViolationException("This KRA is LOCKED. Ask your superior to unlock it before changing definitions.");
        if (kra.Frozen) throw new RuleViolationException("This KRA is frozen.");
        if (kra.PersonId != actor.PersonId && !actor.IsAdministrator) throw new RuleViolationException("Only the owner edits their KRA in SETUP; a superior reviews and approves it.");

        var lines = await _db.TopGoalLines.Where(t => t.Active).Select(t => t.Id).ToListAsync();
        foreach (var input in inputs)
        {
            var slot = kra.Slots.FirstOrDefault(s => s.SlotNumber == input.SlotNumber);
            if (slot is null) { slot = new KraSlot { SlotNumber = input.SlotNumber, KraId = kra.Id }; kra.Slots.Add(slot); }
            var active = !string.IsNullOrWhiteSpace(input.Measure) && !input.Retired;
            if (active && (input.TopGoalLineId is null || !lines.Contains(input.TopGoalLineId.Value)))
                throw new RuleViolationException($"Slot {input.SlotNumber}: choose one of the company Top Goal lines.");
            if (active && input.RatingBasisId is null)
                throw new RuleViolationException($"Slot {input.SlotNumber}: choose a rating basis from the library.");

            Change(slot, "TopGoalLine", slot.TopGoalLineId, input.TopGoalLineId, v => slot.TopGoalLineId = v);
            Change(slot, "DepartmentalGoal", slot.DepartmentalGoal, Clean(input.DepartmentalGoal), v => slot.DepartmentalGoal = v);
            Change(slot, "DepartmentalMeasure", slot.DepartmentalMeasure, Clean(input.DepartmentalMeasure), v => slot.DepartmentalMeasure = v);
            Change(slot, "SectionalGoal", slot.SectionalGoal, Clean(input.SectionalGoal), v => slot.SectionalGoal = v);
            Change(slot, "SectionalMeasure", slot.SectionalMeasure, Clean(input.SectionalMeasure), v => slot.SectionalMeasure = v);
            Change(slot, "Measure", slot.Measure, Clean(input.Measure) ?? "", v => slot.Measure = v ?? "");
            Change(slot, "Target", slot.TargetText, Clean(input.TargetText), v => slot.TargetText = v);
            Change(slot, "Weight", slot.Weight, input.Weight, v => slot.Weight = v);
            Change(slot, "RatingBasis", slot.RatingBasisId, input.RatingBasisId, v => slot.RatingBasisId = v);
            Change(slot, "Aggregation", slot.Aggregation, input.Aggregation, v => slot.Aggregation = v);
            Change(slot, "WeeklyRule", slot.WeeklyRule, input.WeeklyRule, v => slot.WeeklyRule = v);
            Change(slot, "StartValue", slot.StartValue, input.StartValue, v => slot.StartValue = v);
            for (int m = 1; m <= 6; m++) { int mi = m; Change(slot, $"Plan{m}", slot.PlanFor(m), input.Plans.Length >= m ? input.Plans[m - 1] : null, v => slot.SetPlan(mi, v)); }
            Change(slot, "Notes", slot.Notes, Clean(input.Notes), v => slot.Notes = v);
            Change(slot, "Retired", slot.Retired, input.Retired, v => slot.Retired = v);
        }
        kra.UpdatedAt = _clock.UtcNow;
        if (kra.Status == ApprovalStatus.Returned) kra.Status = ApprovalStatus.Draft;
        await _db.SaveChangesAsync();
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Change<T>(KraSlot slot, string field, T oldValue, T newValue, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        apply(newValue);
        _audit.Record("KraSlot", $"{slot.KraId}/{slot.SlotNumber}", "Update", field, oldValue?.ToString(), newValue?.ToString());
    }

    /// <summary>Workflow 1: the person submits. The weight check must read 100%.</summary>
    public async Task SubmitAsync(Kra kra, CurrentUser actor)
    {
        if (kra.PersonId != actor.PersonId) throw new RuleViolationException("Only the owner submits their KRA.");
        if (kra.Mode == DefinitionMode.Locked) throw new RuleViolationException("This KRA is already LOCKED.");
        var (total, ok) = WeightCheck(kra);
        if (!ok) throw new RuleViolationException($"Active weights total {total:0.##}%; they must total exactly 100% before you submit.");
        if (!kra.Slots.Any(s => s.IsActive)) throw new RuleViolationException("Add at least one slot before you submit.");
        kra.Status = ApprovalStatus.Submitted;
        kra.SubmittedAt = _clock.UtcNow;
        kra.ReturnComment = null;
        _audit.Record("Kra", kra.Id.ToString(), "Submit");
        await _db.SaveChangesAsync();
        await _notify.KraSubmittedAsync(kra);
    }

    /// <summary>Approve and lock: records approver and date, bumps the version and snapshots the slots.</summary>
    public async Task ApproveAndLockAsync(Kra kra, CurrentUser approver, string? note = null)
    {
        await RequireSuperiorAsync(kra, approver);
        var (total, ok) = WeightCheck(kra);
        if (!ok) throw new RuleViolationException($"Cannot lock: active weights total {total:0.##}%, not 100%.");
        if (kra.Slots.Where(s => s.IsActive).Any(s => s.TopGoalLineId is null)) throw new RuleViolationException("Cannot lock: every active slot needs a Top Goal line.");
        var old = kra.Mode;
        kra.Mode = DefinitionMode.Locked;
        kra.Status = ApprovalStatus.Approved;
        kra.Version += 1;
        kra.ApprovedByPersonId = approver.PersonId;
        kra.ApprovedByName = approver.Name;
        kra.ApprovedAt = _clock.UtcNow;
        kra.UnlockRequested = false; kra.UnlockReason = null; kra.UnlockRequestedAt = null; kra.UnlockRequestedByPersonId = null;
        kra.UpdatedAt = _clock.UtcNow;
        kra.Versions.Add(new KraVersion { Version = kra.Version, ApprovedByName = approver.Name, ApprovedAt = kra.ApprovedAt.Value, SlotsJson = Snapshot(kra), Note = note });
        _audit.Record("Kra", kra.Id.ToString(), "Lock", "Mode", old.ToString().ToUpperInvariant(), "LOCKED", $"Version {kra.Version} approved by {approver.Name}");
        await _db.SaveChangesAsync();
        await _notify.KraApprovedAsync(kra);
    }

    /// <summary>Lock by migration or seed: approver "migration", version 1.</summary>
    public async Task LockAsSystemAsync(Kra kra, string approverName, string? note = null)
    {
        kra.Mode = DefinitionMode.Locked; kra.Status = ApprovalStatus.Approved; kra.Version += 1;
        kra.ApprovedByName = approverName; kra.ApprovedAt = _clock.UtcNow; kra.UpdatedAt = _clock.UtcNow;
        kra.Versions.Add(new KraVersion { Version = kra.Version, ApprovedByName = approverName, ApprovedAt = kra.ApprovedAt.Value, SlotsJson = Snapshot(kra), Note = note });
        _audit.Record("Kra", kra.Id.ToString(), "Lock", "Mode", "SETUP", "LOCKED", $"Version {kra.Version} approved by {approverName}");
        await _db.SaveChangesAsync();
    }

    public async Task ReturnAsync(Kra kra, CurrentUser approver, string comment)
    {
        await RequireSuperiorAsync(kra, approver);
        kra.Status = ApprovalStatus.Returned;
        kra.ReturnComment = string.IsNullOrWhiteSpace(comment) ? "Returned for changes." : comment.Trim();
        kra.UpdatedAt = _clock.UtcNow;
        _audit.Record("Kra", kra.Id.ToString(), "Return", detail: kra.ReturnComment);
        await _db.SaveChangesAsync();
        await _notify.KraReturnedAsync(kra);
    }

    /// <summary>The person asks their superior to unlock (open decision: who may unlock; the superior decides here).</summary>
    public async Task RequestUnlockAsync(Kra kra, CurrentUser actor, string reason)
    {
        if (kra.PersonId != actor.PersonId) throw new RuleViolationException("Only the owner requests an unlock.");
        if (kra.Mode != DefinitionMode.Locked) throw new RuleViolationException("This KRA is not locked.");
        if (string.IsNullOrWhiteSpace(reason)) throw new RuleViolationException("Say why the KRA should be unlocked.");
        kra.UnlockRequested = true; kra.UnlockReason = reason.Trim(); kra.UnlockRequestedAt = _clock.UtcNow; kra.UnlockRequestedByPersonId = actor.PersonId;
        _audit.Record("Kra", kra.Id.ToString(), "UnlockRequest", detail: kra.UnlockReason);
        await _db.SaveChangesAsync();
        await _notify.UnlockRequestedAsync(kra);
    }

    /// <summary>Workflow 5: unlock → edit in SETUP → re-approve. The next lock creates the next version; weekly results stay attached to the slots.</summary>
    public async Task UnlockAsync(Kra kra, CurrentUser approver, string? reason)
    {
        await RequireSuperiorAsync(kra, approver);
        if (kra.Mode != DefinitionMode.Locked) throw new RuleViolationException("This KRA is not locked.");
        kra.Mode = DefinitionMode.Setup;
        kra.Status = ApprovalStatus.Draft;
        kra.UnlockRequested = false;
        kra.UpdatedAt = _clock.UtcNow;
        _audit.Record("Kra", kra.Id.ToString(), "Unlock", "Mode", "LOCKED", "SETUP", string.IsNullOrWhiteSpace(reason) ? kra.UnlockReason : reason);
        await _db.SaveChangesAsync();
        await _notify.KraUnlockedAsync(kra);
    }

    /// <summary>Keeps a KRA LOCKED after a declined unlock request: no new version, status back to Approved.</summary>
    public async Task LockAsSystemNoVersionAsync(Kra kra)
    {
        kra.Mode = DefinitionMode.Locked; kra.Status = ApprovalStatus.Approved; kra.UnlockRequested = false; kra.UpdatedAt = _clock.UtcNow;
        _audit.Record("Kra", kra.Id.ToString(), "UnlockDeclined");
        await _db.SaveChangesAsync();
    }

    /// <summary>A leaver's KRA is frozen with its results (workflow 6).</summary>
    public async Task FreezeAsync(Kra kra, string reason)
    {
        kra.Frozen = true; kra.UpdatedAt = _clock.UtcNow;
        _audit.Record("Kra", kra.Id.ToString(), "Freeze", detail: reason);
        await _db.SaveChangesAsync();
    }

    private async Task RequireSuperiorAsync(Kra kra, CurrentUser approver)
    {
        var person = kra.Person ?? await _org.LoadPersonAsync(kra.PersonId) ?? throw new RuleViolationException("Person not found.");
        if (approver.IsAdministrator) return;
        if (!await _org.IsSuperiorOfAsync(approver, person)) throw new RuleViolationException("Only the person's superior approves, locks or unlocks this KRA.");
    }

    public static string Snapshot(Kra kra) => JsonSerializer.Serialize(kra.Slots.Where(s => s.IsActive).OrderBy(s => s.SlotNumber).Select(s => new
    {
        s.SlotNumber, s.TopGoalLineId, TopGoal = s.TopGoalLine?.Measure, s.DepartmentalGoal, s.DepartmentalMeasure, s.SectionalGoal, s.SectionalMeasure,
        s.Measure, s.TargetText, s.Weight, s.RatingBasisId, Basis = s.RatingBasis?.Key, Aggregation = s.Aggregation.ToString(), WeeklyRule = s.WeeklyRule.ToString(),
        s.StartValue, Plans = new[] { s.Plan1, s.Plan2, s.Plan3, s.Plan4, s.Plan5, s.Plan6 }, s.Notes,
    }));

    /// <summary>KRAs waiting on a superior: submitted, unlock requested, or never locked after the first week of the half.</summary>
    public async Task<List<Kra>> PendingForAsync(CurrentUser user, FiscalHalf half)
    {
        var people = await _org.VisiblePeopleAsync(user);
        var ids = people.Where(p => p.Id != user.PersonId).Select(p => p.Id).ToList();
        return await FullKras().Where(k => k.FiscalHalfId == half.Id && ids.Contains(k.PersonId) && !k.Frozen).OrderBy(k => k.Person!.Position!.SortOrder).ThenBy(k => k.Person!.Name).ToListAsync();
    }

    public async Task<List<AuditEntry>> ChangeTrailAsync(Kra kra, int take = 200)
    {
        var slotPrefix = $"{kra.Id}/";
        var id = kra.Id.ToString();
        return await _db.AuditEntries
            .Where(a => (a.Entity == "Kra" && a.EntityId == id) || (a.Entity == "KraSlot" && a.EntityId.StartsWith(slotPrefix)) || (a.Entity == "WeeklyResult" && a.EntityId.StartsWith(slotPrefix)) || (a.Entity == "MonthlyManualValue" && a.EntityId.StartsWith(slotPrefix)) || (a.Entity == "Remark" && a.EntityId.StartsWith(slotPrefix)))
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(take).ToListAsync();
    }

    public Task<Dictionary<int, RatingBasisDefinition>> BasisLookupAsync()
        => _db.RatingBases.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.ToDefinition());
}
