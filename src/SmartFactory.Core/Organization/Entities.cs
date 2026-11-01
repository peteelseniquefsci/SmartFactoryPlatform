namespace SmartFactory.Core.Organization;

/// <summary>A department of the company (Smart Factory is the first).</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Company { get; set; } = "First Sumiden Circuits Inc.";
    public List<Section> Sections { get; set; } = new();
}

/// <summary>A section inside a department. Carries the category ownership used by the module Scores.</summary>
public class Section
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    /// <summary>Free text owned by modules, e.g. which company Scores and categories the section owns.</summary>
    public string? Ownership { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Parent section for a hierarchy (Design &amp; Project sits under Digital Systems).</summary>
    public int? ParentSectionId { get; set; }
    public Section? ParentSection { get; set; }
    public List<Position> Positions { get; set; } = new();
}

/// <summary>A position on the table of organization. A position may have many incumbents; a vacancy is a position without a person.</summary>
public class Position
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int? ReportsToPositionId { get; set; }
    public Position? ReportsTo { get; set; }
    /// <summary>Used when the superior is outside the department (e.g. General Manager).</summary>
    public string? ExternalReportsTo { get; set; }
    /// <summary>Seats per table of organization.</summary>
    public int Seats { get; set; } = 1;
    /// <summary>Head of a section: may approve, lock and unlock the KRAs of positions beneath.</summary>
    public bool IsSectionHead { get; set; }
    /// <summary>Manager level (Digital Systems Manager, Smart Factory Manager).</summary>
    public bool IsManager { get; set; }
    public int SortOrder { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public List<Person> People { get; set; } = new();
}

public enum Assignment { None = 0, Customer = 1, Enterprise = 2 }

/// <summary>A person. Employee id and name come from the company employee-information API.</summary>
public class Person
{
    public int Id { get; set; }
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public int PositionId { get; set; }
    public Position? Position { get; set; }
    public Assignment Assignment { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool Active { get; set; } = true;
    public List<RoleAssignment> Roles { get; set; } = new();
}

/// <summary>Platform roles. Person, SectionHead and Manager derive from the organization; HrViewer and Administrator are assigned.</summary>
public enum PlatformRole { Person = 0, SectionHead = 1, Manager = 2, HrViewer = 3, Administrator = 4 }

public class RoleAssignment
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public PlatformRole Role { get; set; }
    public string GrantedBy { get; set; } = "";
    public DateTime GrantedAt { get; set; }
}
