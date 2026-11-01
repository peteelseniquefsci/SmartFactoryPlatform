# Smart Factory Platform

Replaces the Smart Factory department's Excel scorecards at First Sumiden Circuits Inc. with one application.
Module 1 is Performance Management (cascaded Balanced Scorecard, 2H FY2026). Built to the hand-off
specification (sections 3 to 11) and the "Ascent" design system.

Stack: .NET 8 LTS, ASP.NET Core MVC, Entity Framework Core 8 with MySQL (Pomelo provider), Tailwind CSS v4 (compiled
once, committed), ClosedXML for the Excel export, xUnit for the 22 acceptance tests.

## Solution layout

| Project | What it holds |
| --- | --- |
| `src/SmartFactory.Core` | The platform core: organization (departments, sections, positions, people, roles), periods (fiscal year, halves, months, ISO work weeks and the week-to-month rule), identity abstraction (company LDAP API), versioned definitions (SETUP / LOCKED), audit trail, email abstraction. Nothing here knows about scorecards. |
| `src/SmartFactory.Performance` | Module 1: KRA, slots, weekly results, manual values, remarks, versions, position templates; the rule engine (`Rules/RuleEngine.cs`, section 5 exactly as written); the evaluator that derives every value; the Target Indices export layout. |
| `src/SmartFactory.Infrastructure` | EF Core `SmartFactoryDbContext` for MySQL plus migrations, services (organization scope, periods, KRA workflow, weekly entry, evaluation queries, notifications), Excel export, CSV import, template import, seed data, identity and email providers. |
| `src/SmartFactory.Web` | The MVC app: sign-in, Ascent, My KRA, My week, Team view, Cascade, Week by week, Approvals, Exports, Admin. Tailwind theme in `Styles/app.css`, fonts bundled under `wwwroot/fonts`. |
| `tests/SmartFactory.Tests` | Acceptance tests 1 to 18 on the pure rule engine, 19 to 22 on the services with SQLite in memory, plus permission and joiner/leaver tests. |

## Run locally

Prerequisites: .NET 8 SDK, MySQL 8 (or MariaDB 10.11), Node 18+ only if you change the styles.

```bash
mysql -uroot -e "CREATE DATABASE smartfactory CHARACTER SET utf8mb4; CREATE USER 'sf'@'localhost' IDENTIFIED BY 'sf'; GRANT ALL ON smartfactory.* TO 'sf'@'localhost';"
```

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SmartFactory.Web --urls http://localhost:5080
```

On start the app applies migrations and seeds section 6 of the spec (organization, 2H FY2026 calendar, Top Goal lines,
rating basis library, position templates). In Development it also seeds demo people (DEMO-001 to DEMO-027, DEMO-HR) and
October to mid-November demo actuals, pins the clock to 11 Nov 2026 (`Clock:Today`), signs in with a person picker,
and writes emails to the log. Sign in as DEMO-001 (Smart Factory Manager, also Administrator), DEMO-003 (a section head)
or DEMO-004 (an engineer) to see the three roles.

```bash
dotnet test
```

## Configuration (appsettings.json)

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:SmartFactory` | MySQL connection string. |
| `Database:ServerVersion` | e.g. `8.0.36-mysql` or `10.11.14-mariadb`. Leave empty to auto-detect at start. |
| `Database:MigrateOnStartup` | Apply EF migrations at start (default true). |
| `Identity:Provider` | `CompanyApi` (production: sign-in through the company LDAP API over HTTP; never a direct LDAP bind) or `Development`. |
| `Email:Provider` | `ExchangeApi` (the single permitted Exchange API address) or `Logging`. |
| `CompanyApi:*` | Base URL and paths of the identity API, the Exchange API URL and sender. The identity API is expected to answer `{ employeeId, displayName, email }`. |
| `Seed:DemoPeople`, `Seed:DemoActuals` | Development only. Production people come from the company API via Admin → Organization. |
| `Clock:Today` | Pin "today" for demos and parallel runs; remove in production. |

## Deployment on the FSCI LAN (no internet)

- Dependencies are NuGet packages and can be restored from the internal mirror; `dotnet publish -c Release` produces a
  self-contained folder. No runtime call leaves the LAN: fonts are bundled (`wwwroot/fonts`, SIL OFL), the CSS is
  compiled and committed (`wwwroot/css/app.css`), there are no CDN references.
- To change styles: `cd src/SmartFactory.Web && npm install && npm run css`, then commit the output. Tailwind is a
  build-time tool only.
- Migrations: `dotnet ef database update --project src/SmartFactory.Infrastructure --startup-project src/SmartFactory.Web`
  or let the app migrate on start. A new migration: `dotnet ef migrations add <Name> --project src/SmartFactory.Infrastructure --startup-project src/SmartFactory.Infrastructure --output-dir Data/Migrations` (set `SMARTFACTORY_SERVERVERSION` if not MySQL 8).
- Nightly backup: `mysqldump smartfactory`; every change is in the `AuditEntries` table with who, when, old and new value.
- Roles: Person, Section head and Manager derive from the position flags in Admin → Organization; Administrator and HR
  viewer are assigned there. Permissions follow the reporting line.

## Migration from the Excel system

Order (spec section 12): organization and people → periods → Top Goal lines and rating library (seeded) → KRAs and slots
from the definitions workbook → weekly actuals, manual values and remarks → reconcile monthly totals.

1. Convert `_builders/spec.json` to the template JSON format (one entry per position, slots by number; see
   `src/SmartFactory.Infrastructure/Seed/kra-templates.json` and Admin → Imports) and import it. Each person's KRA is then
   created from their position's template on first sign-in, or lock them as version 1 with approver "migration" using
   `KraService.LockAsSystemAsync`.
2. Import weekly actuals as CSV (`employee_id, slot, week, actual, source_row_id[, manual_target]`) from the personal
   files' Export block. Imports are idempotent on (slot, week) and audited; typed values are kept unless you tick overwrite.
3. Cut-over check: compare each person's Oct and Nov totals on My KRA or Exports with the personal file's TOTAL row.

## Rules and tests

Section 5 is implemented in `RuleEngine` and verified by `tests/SmartFactory.Tests/AcceptanceTests.cs` (cases 1 to 18)
and `WorkflowTests.cs` (19 to 22). Weight is stored in percent; weighted score = rating × weight / 100 so 4 × 5% = 0.20.
Month status follows 5.7 exactly: in the weekly view a month is final once the viewed week is after the month's last
week; in the monthly view once the month's last week has an actual (or the MANUAL slot has its monthly value).

## Assumptions and open points carried into this build

- Seeded KRA templates: the Smart Factory Manager template is exact from spec 6.2 and 6.6. The Digital Systems Manager
  and the four section heads get only their Score slots with the interim company ramp (spec 6.2, marked in the notes).
  Every other slot comes from `Smart_Factory_2H_FY2026_Scorecards.xlsx`, which was not available to this session; import
  it through Admin → Imports. In the demo seed, head KRAs are padded with a clearly labelled placeholder closure slot so they lock.
- Top Goal line for outages and downtime assumed to be Delivery (LLPFAC); validated savings plans left blank until the
  savings tracker plan is known.
- Unlocking a LOCKED KRA: the person requests, the superior decides (open decision in spec 13). An administrator may also act.
- The Target Indices export reproduces the columns listed in the spec; matching the company form cell by cell needs the
  definitions workbook as a template and is the first thing to verify at parallel run.
- The two company Scores on Ascent are read from the Compliance / Factory slots on the department manager's KRA.
