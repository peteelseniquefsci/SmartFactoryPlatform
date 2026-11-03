# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Smart Factory Platform: a .NET 8 ASP.NET Core MVC application with MySQL (EF Core 8, Pomelo) that replaces the Smart Factory
department's Excel scorecards at First Sumiden Circuits Inc. Module 1 is Performance Management (cascaded Balanced Scorecard,
2H FY2026). The contract is `docs/hand-off-specification.md`: section 5 holds the business rules (implemented exactly as written
and verified by the acceptance tests in section 11), section 6 the seed data, section 7 roles and workflows, section 9 the UX,
section 10 the LAN constraints. The look is the "Ascent" design system, summarised in `docs/design-system.md`.

Two rules from the spec that shape every change:

- Nothing in `SmartFactory.Core` may assume scorecards; the word "scorecard"/KRA belongs to `SmartFactory.Performance`.
  Later modules (Registers, Work intake) will reuse the core.
- A rule in section 5 is never changed without changing the matching acceptance test. If a deviation is needed, write it as a
  numbered rule change with the test it alters and record it in the README "Assumptions" list.

## Commands

The .NET 8 SDK is required (in the cloud session it lives at `/home/user/.tools/dotnet`; export `DOTNET_ROOT` and add it to `PATH`).

```bash
dotnet build                                   # whole solution
dotnet test                                    # 26 tests: acceptance 1-18 on the pure engine, 19-22 on the services (SQLite in memory)
dotnet test --filter "DisplayName~17."         # one acceptance case by its number
dotnet test --filter "FullyQualifiedName~WorkflowTests"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SmartFactory.Web --urls http://localhost:5080
docker compose up --build                      # MySQL 8 + app in Development mode on :8080
```

Development mode needs a local MySQL/MariaDB with database `smartfactory`, user `sf`/`sf` (see README). It migrates and seeds on
start, seeds demo people (DEMO-001 manager/admin, DEMO-003 a section head, DEMO-004 an engineer, DEMO-HR), pins the clock to
11 Nov 2026 via `Clock:Today`, and signs in with a person picker (no password).

Styles: Tailwind v4 is build-time only. After changing `src/SmartFactory.Web/Styles/app.css` or adding utility classes in views,
rebuild and commit the output (the LAN deployment has no npm):

```bash
cd src/SmartFactory.Web && npm install && npm run css
```

A stale `wwwroot/css/app.css` shows up as unstyled layout (Tailwind only emits classes it finds in `Views/` and `Controllers/`).

Migrations (the design-time factory reads `SMARTFACTORY_CONNECTION` / `SMARTFACTORY_SERVERVERSION`, default MySQL 8):

```bash
dotnet ef migrations add <Name> --project src/SmartFactory.Infrastructure --startup-project src/SmartFactory.Infrastructure --output-dir Data/Migrations
```

Production host is IIS on Windows Server: `dotnet publish src/SmartFactory.Web -c Release -r win-x64 --self-contained false`
(see README). Every link must stay application-relative (`href="~/..."`, `Redirect(Url.Content("~/..."))`) so the app works as
an IIS sub-application; never write `href="/..."` or `Redirect("/...")`.

## Architecture

Five projects, dependencies flow downward:

- `src/SmartFactory.Core` – organization (Department → Section → Position → Person, `OrganizationTree` for the reporting line),
  periods (`FiscalCalendar.Build` generates a half's months and ISO work weeks; rule 5.1: a week belongs to the month of its
  Thursday, k/n stored per week), identity (`IIdentityProvider`, `CurrentUser`, `RoleResolver`: Person always, SectionHead and
  Manager from position flags, HrViewer and Administrator from `RoleAssignment`), `IVersionedDefinition` (SETUP/LOCKED), audit
  (`IAuditTrail`), `IEmailSender`.
- `src/SmartFactory.Performance` – entities (`Kra`, `KraSlot` 1–20 with Plan1..Plan6, `WeeklyResult` with `Source`,
  `MonthlyManualValue`, `Remark`, `KraVersion` snapshot JSON, `PositionTemplateSlot`), `Rules/RuleEngine.cs` (pure functions, one
  per spec rule 5.2–5.8), `Evaluation/KraEvaluator.cs` (turns a loaded KRA plus the calendar into every derived value for a
  `EvaluationContext`: `Monthly(half)` or `WeeklyAt(half, week)`; "final" differs between the two views exactly as 5.7 says),
  `Export/TargetIndicesLayout.cs` (row model of the HR form).
- `src/SmartFactory.Infrastructure` – `SmartFactoryDbContext` (MySQL; decimals have explicit precision), `Services/`:
  `OrganizationService` (visibility and superior checks walk the position tree), `PeriodService` (current half, "this week",
  honours `IClock`), `KraService` (get-or-create from the position template, `SaveSlotsAsync` with per-field audit, submit,
  approve-and-lock which bumps `Version` and snapshots slots, return, unlock request/decision, freeze), `WeeklyEntryService`,
  `EvaluationService` (loads full KRAs with `KraService.FullKras()` and re-attaches the loaded calendar before evaluating),
  `NotificationService`; `Seed/Seeder` (spec section 6, idempotent) and `Seed/kra-templates.json` (the JSON shape the
  definitions workbook is converted into), `Import/WeeklyResultImporter` (CSV, idempotent on slot+week), `Export/ExcelExportService`
  (ClosedXML), identity providers (`DevelopmentIdentityProvider`, `LdapApiIdentityProvider` over HTTP, never a direct LDAP bind),
  email senders (`LoggingEmailSender`, `ExchangeApiEmailSender`). `DependencyInjection.AddSmartFactoryInfrastructure` wires it
  from configuration (`Identity:Provider`, `Email:Provider`, `Clock:Today`).
- `src/SmartFactory.Web` – cookie auth; `WebCurrentUserAccessor` loads the `Person` once per request from the `sf:pid` claim and
  `CurrentUserMiddleware` re-derives roles from the organization on every request; `RuleViolationFilter` turns a
  `RuleViolationException` thrown by a service into a `TempData["Error"]` flash and redirects back. Controllers map to the
  spec's screens (Ascent = home, Kra = My KRA, Week = My week, Team, Cascade, WeekByWeek, Approvals, Exports, Admin);
  authorization policies are in `Infrastructure/WebCurrentUserAccessor.cs` (`Policies`). Views are Razor with Tailwind
  utilities; shared components are partials in `Views/Shared` (`_StatusChip`, `_JudgementChip`, `_Rating`, `_ModeBadge`,
  `_MonthChip`); SVG `<text>` must go through `Svg.Text(...)` in `Infrastructure/Fmt.cs` because Razor reserves the tag.
  Formatting (dd Mmm yyyy, Met / — / Unmet spellings) is in `Fmt`.
- `tests/SmartFactory.Tests` – `AcceptanceTests.cs` (engine, uses `TestData` builders), `WorkflowTests.cs` (real services on
  SQLite in memory with the seeder).

Conventions that are easy to get wrong:

- Weights are stored in percent; weighted score = rating × weight / 100 (4 × 5 % = 0.20). Monthly total is complete only when
  every active slot has a rating; otherwise it is shown as "provisional" (amber), never hidden.
- Derived values are never stored. Weekly results stay attached to `KraSlot.Id`; retiring a slot (`Retired = true`) keeps its
  history. Versions are snapshots made at each lock; unlock does not bump the version.
- A rating basis is the only place bands are defined (library in `RatingBases`); thresholds 9 (RH/RL) or −1 (AL) make a band
  unreachable. `RatingMode.M` means a typed 1–5 rating from `MonthlyManualValue`.
- Audit: every definition, approval, lock, result and remark change is recorded through `IAuditTrail` with old and new value;
  services call `_audit.Record(...)` before `SaveChangesAsync`.
- No external network at runtime: no CDN, no Google Fonts; fonts are in `wwwroot/fonts`.
