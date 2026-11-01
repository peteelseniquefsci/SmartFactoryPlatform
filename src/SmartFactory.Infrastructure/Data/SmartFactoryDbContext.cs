using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Data;

public class SmartFactoryDbContext : DbContext
{
    public SmartFactoryDbContext(DbContextOptions<SmartFactoryDbContext> options) : base(options) { }

    // Core
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<FiscalHalf> FiscalHalves => Set<FiscalHalf>();
    public DbSet<FiscalMonth> FiscalMonths => Set<FiscalMonth>();
    public DbSet<WorkWeek> WorkWeeks => Set<WorkWeek>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // Module 1: Performance Management
    public DbSet<TopGoalLine> TopGoalLines => Set<TopGoalLine>();
    public DbSet<RatingBasis> RatingBases => Set<RatingBasis>();
    public DbSet<Kra> Kras => Set<Kra>();
    public DbSet<KraSlot> KraSlots => Set<KraSlot>();
    public DbSet<WeeklyResult> WeeklyResults => Set<WeeklyResult>();
    public DbSet<MonthlyManualValue> MonthlyManualValues => Set<MonthlyManualValue>();
    public DbSet<Remark> Remarks => Set<Remark>();
    public DbSet<KraVersion> KraVersions => Set<KraVersion>();
    public DbSet<PositionTemplateSlot> PositionTemplateSlots => Set<PositionTemplateSlot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Department>(e => { e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Company).HasMaxLength(200); });
        b.Entity<Section>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Ownership).HasMaxLength(500);
            e.HasOne(x => x.ParentSection).WithMany().HasForeignKey(x => x.ParentSectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany(d => d.Sections).HasForeignKey(x => x.DepartmentId);
        });
        b.Entity<Position>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(150); e.Property(x => x.ExternalReportsTo).HasMaxLength(150);
            e.HasOne(x => x.ReportsTo).WithMany().HasForeignKey(x => x.ReportsToPositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Section).WithMany(s => s.Positions).HasForeignKey(x => x.SectionId);
        });
        b.Entity<Person>(e =>
        {
            e.Property(x => x.EmployeeId).HasMaxLength(40); e.HasIndex(x => x.EmployeeId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Email).HasMaxLength(200);
            e.HasOne(x => x.Position).WithMany(p => p.People).HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RoleAssignment>(e => { e.Property(x => x.GrantedBy).HasMaxLength(150); e.HasOne(x => x.Person).WithMany(p => p.Roles).HasForeignKey(x => x.PersonId); e.HasIndex(x => new { x.PersonId, x.Role }).IsUnique(); });

        b.Entity<FiscalHalf>(e => { e.Property(x => x.Label).HasMaxLength(20); e.HasIndex(x => new { x.FiscalYear, x.Half }).IsUnique(); });
        b.Entity<FiscalMonth>(e => { e.Property(x => x.Label).HasMaxLength(20); e.HasOne(x => x.FiscalHalf).WithMany(h => h.Months).HasForeignKey(x => x.FiscalHalfId); e.HasIndex(x => new { x.FiscalHalfId, x.Index }).IsUnique(); });
        b.Entity<WorkWeek>(e => { e.Property(x => x.Label).HasMaxLength(10); e.HasOne(x => x.FiscalMonth).WithMany(m => m.Weeks).HasForeignKey(x => x.FiscalMonthId); e.HasIndex(x => new { x.IsoYear, x.IsoWeek }).IsUnique(); });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.ActorName).HasMaxLength(150); e.Property(x => x.Entity).HasMaxLength(60); e.Property(x => x.EntityId).HasMaxLength(60);
            e.Property(x => x.Action).HasMaxLength(30); e.Property(x => x.Field).HasMaxLength(60);
            e.Property(x => x.OldValue).HasMaxLength(2000); e.Property(x => x.NewValue).HasMaxLength(2000); e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => new { x.Entity, x.EntityId }); e.HasIndex(x => x.At);
        });

        b.Entity<TopGoalLine>(e => { e.Property(x => x.FocusItem).HasMaxLength(120); e.Property(x => x.StrategicObjective).HasMaxLength(200); e.Property(x => x.Measure).HasMaxLength(200); });
        b.Entity<RatingBasis>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(40); e.HasIndex(x => x.Key).IsUnique(); e.Property(x => x.Name).HasMaxLength(120);
            foreach (var p in new[] { nameof(RatingBasis.T5), nameof(RatingBasis.T4), nameof(RatingBasis.T3), nameof(RatingBasis.T2) }) e.Property(p).HasPrecision(18, 6);
            e.Property(x => x.Notes).HasMaxLength(500); e.Property(x => x.UsedFor).HasMaxLength(300);
            foreach (var p in new[] { "Band5Text", "Band4Text", "Band3Text", "Band2Text", "Band1Text" }) e.Property(p).HasMaxLength(200);
        });
        b.Entity<Kra>(e =>
        {
            e.HasIndex(x => new { x.PersonId, x.FiscalHalfId }).IsUnique();
            e.Property(x => x.ApprovedByName).HasMaxLength(150); e.Property(x => x.ReturnComment).HasMaxLength(2000); e.Property(x => x.UnlockReason).HasMaxLength(2000);
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FiscalHalf).WithMany().HasForeignKey(x => x.FiscalHalfId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<KraSlot>(e =>
        {
            e.HasIndex(x => new { x.KraId, x.SlotNumber }).IsUnique();
            e.HasOne(x => x.Kra).WithMany(k => k.Slots).HasForeignKey(x => x.KraId);
            e.HasOne(x => x.TopGoalLine).WithMany().HasForeignKey(x => x.TopGoalLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RatingBasis).WithMany().HasForeignKey(x => x.RatingBasisId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Measure).HasMaxLength(500); e.Property(x => x.TargetText).HasMaxLength(300); e.Property(x => x.Notes).HasMaxLength(1000);
            foreach (var p in new[] { "DepartmentalGoal", "DepartmentalMeasure", "SectionalGoal", "SectionalMeasure" }) e.Property(p).HasMaxLength(500);
            e.Property(x => x.Weight).HasPrecision(7, 3);
            foreach (var p in new[] { "StartValue", "Plan1", "Plan2", "Plan3", "Plan4", "Plan5", "Plan6" }) e.Property(p).HasPrecision(18, 6);
            e.Ignore(x => x.Plans);
        });
        b.Entity<WeeklyResult>(e =>
        {
            e.HasIndex(x => new { x.KraSlotId, x.WorkWeekId }).IsUnique();
            e.HasOne(x => x.KraSlot).WithMany(s => s.WeeklyResults).HasForeignKey(x => x.KraSlotId);
            e.HasOne(x => x.WorkWeek).WithMany().HasForeignKey(x => x.WorkWeekId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Actual).HasPrecision(18, 6); e.Property(x => x.ManualWeeklyTarget).HasPrecision(18, 6);
            e.Property(x => x.SourceRowId).HasMaxLength(120);
        });
        b.Entity<MonthlyManualValue>(e =>
        {
            e.HasIndex(x => new { x.KraSlotId, x.FiscalMonthId }).IsUnique();
            e.HasOne(x => x.KraSlot).WithMany(s => s.MonthlyManualValues).HasForeignKey(x => x.KraSlotId);
            e.HasOne(x => x.FiscalMonth).WithMany().HasForeignKey(x => x.FiscalMonthId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ManualActual).HasPrecision(18, 6);
        });
        b.Entity<Remark>(e =>
        {
            e.HasOne(x => x.KraSlot).WithMany(s => s.Remarks).HasForeignKey(x => x.KraSlotId);
            e.HasOne(x => x.WorkWeek).WithMany().HasForeignKey(x => x.WorkWeekId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FiscalMonth).WithMany().HasForeignKey(x => x.FiscalMonthId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorPersonId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Text).HasMaxLength(2000);
        });
        b.Entity<KraVersion>(e => { e.HasOne(x => x.Kra).WithMany(k => k.Versions).HasForeignKey(x => x.KraId); e.Property(x => x.ApprovedByName).HasMaxLength(150); e.Property(x => x.Note).HasMaxLength(500); e.HasIndex(x => new { x.KraId, x.Version }).IsUnique(); });
        b.Entity<PositionTemplateSlot>(e =>
        {
            e.HasIndex(x => new { x.PositionId, x.SlotNumber }).IsUnique();
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId);
            e.Property(x => x.Measure).HasMaxLength(500); e.Property(x => x.TargetText).HasMaxLength(300); e.Property(x => x.Notes).HasMaxLength(1000);
            foreach (var p in new[] { "DepartmentalGoal", "DepartmentalMeasure", "SectionalGoal", "SectionalMeasure" }) e.Property(p).HasMaxLength(500);
            e.Property(x => x.Weight).HasPrecision(7, 3);
            foreach (var p in new[] { "StartValue", "Plan1", "Plan2", "Plan3", "Plan4", "Plan5", "Plan6" }) e.Property(p).HasPrecision(18, 6);
        });
    }
}
