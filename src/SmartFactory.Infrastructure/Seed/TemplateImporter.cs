using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Seed;

public class TemplateFile { [JsonPropertyName("positions")] public List<TemplatePosition> Positions { get; set; } = new(); }
public class TemplatePosition { [JsonPropertyName("position")] public string Position { get; set; } = ""; [JsonPropertyName("slots")] public List<TemplateSlot> Slots { get; set; } = new(); }
public class TemplateSlot
{
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("topGoal")] public string? TopGoal { get; set; }
    [JsonPropertyName("departmentalGoal")] public string? DepartmentalGoal { get; set; }
    [JsonPropertyName("departmentalMeasure")] public string? DepartmentalMeasure { get; set; }
    [JsonPropertyName("sectionalGoal")] public string? SectionalGoal { get; set; }
    [JsonPropertyName("sectionalMeasure")] public string? SectionalMeasure { get; set; }
    [JsonPropertyName("measure")] public string Measure { get; set; } = "";
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("weight")] public decimal Weight { get; set; }
    [JsonPropertyName("basis")] public string? Basis { get; set; }
    [JsonPropertyName("aggregation")] public string Aggregation { get; set; } = "LAST";
    [JsonPropertyName("rule")] public string Rule { get; set; } = "RAMP";
    [JsonPropertyName("start")] public decimal? Start { get; set; }
    [JsonPropertyName("plans")] public decimal?[] Plans { get; set; } = new decimal?[6];
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

/// <summary>Loads position templates (the definitions workbook converted to JSON). Idempotent per position and slot number.</summary>
public class TemplateImporter
{
    private readonly SmartFactoryDbContext _db;
    private readonly IAuditTrail _audit;
    public TemplateImporter(SmartFactoryDbContext db, IAuditTrail audit) { _db = db; _audit = audit; }

    public async Task<(int positions, int slots, List<string> errors)> ImportAsync(string json)
    {
        var file = JsonSerializer.Deserialize<TemplateFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new TemplateFile();
        var positions = await _db.Positions.ToListAsync();
        var lines = await _db.TopGoalLines.ToListAsync();
        var bases = await _db.RatingBases.ToListAsync();
        var existing = await _db.PositionTemplateSlots.ToListAsync();
        var errors = new List<string>();
        int pc = 0, sc = 0;
        foreach (var tp in file.Positions)
        {
            var pos = positions.FirstOrDefault(p => p.Title.Equals(tp.Position, StringComparison.OrdinalIgnoreCase));
            if (pos is null) { errors.Add($"Position '{tp.Position}' not found."); continue; }
            pc++;
            foreach (var ts in tp.Slots)
            {
                var line = lines.FirstOrDefault(l => l.Measure.Equals(ts.TopGoal ?? "", StringComparison.OrdinalIgnoreCase));
                if (line is null && !string.IsNullOrWhiteSpace(ts.TopGoal)) { errors.Add($"{tp.Position} slot {ts.Slot}: Top Goal '{ts.TopGoal}' is not one of the 16 company lines."); continue; }
                var basis = bases.FirstOrDefault(b => b.Key.Equals(ts.Basis ?? "", StringComparison.OrdinalIgnoreCase));
                if (basis is null && !string.IsNullOrWhiteSpace(ts.Basis)) { errors.Add($"{tp.Position} slot {ts.Slot}: rating basis '{ts.Basis}' not in the library."); continue; }
                if (!Enum.TryParse<Aggregation>(ts.Aggregation, true, out var agg)) { errors.Add($"{tp.Position} slot {ts.Slot}: aggregation '{ts.Aggregation}'."); continue; }
                if (!Enum.TryParse<WeeklyRule>(ts.Rule, true, out var rule)) { errors.Add($"{tp.Position} slot {ts.Slot}: weekly rule '{ts.Rule}'."); continue; }
                var row = existing.FirstOrDefault(e => e.PositionId == pos.Id && e.SlotNumber == ts.Slot);
                if (row is null) { row = new PositionTemplateSlot { PositionId = pos.Id, SlotNumber = ts.Slot }; _db.PositionTemplateSlots.Add(row); existing.Add(row); }
                row.TopGoalLineId = line?.Id; row.DepartmentalGoal = ts.DepartmentalGoal; row.DepartmentalMeasure = ts.DepartmentalMeasure; row.SectionalGoal = ts.SectionalGoal; row.SectionalMeasure = ts.SectionalMeasure;
                row.Measure = ts.Measure; row.TargetText = ts.Target; row.Weight = ts.Weight; row.RatingBasisId = basis?.Id; row.Aggregation = agg; row.WeeklyRule = rule; row.StartValue = ts.Start;
                var plans = ts.Plans ?? new decimal?[6];
                row.Plan1 = plans.ElementAtOrDefault(0); row.Plan2 = plans.ElementAtOrDefault(1); row.Plan3 = plans.ElementAtOrDefault(2); row.Plan4 = plans.ElementAtOrDefault(3); row.Plan5 = plans.ElementAtOrDefault(4); row.Plan6 = plans.ElementAtOrDefault(5);
                row.Notes = ts.Notes;
                sc++;
            }
        }
        _audit.Record("PositionTemplate", "all", "Import", detail: $"{pc} positions, {sc} slots");
        await _db.SaveChangesAsync();
        return (pc, sc, errors);
    }
}
