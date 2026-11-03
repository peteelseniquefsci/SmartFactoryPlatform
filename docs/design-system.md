# Design system notes ("Ascent")

Source of truth: the Claude Design artifact https://claude.ai/artifact/Sav29QZzgwyTQrSFde3ujy (tokens.json, README, components).
The tokens are mirrored in `src/SmartFactory.Web/Styles/app.css` (`:root` for Midnight, `:root[data-theme="light"]` for Light,
then `@theme inline` so Tailwind utilities such as `bg-surface`, `text-ink-muted`, `text-copper-text`, `bg-moss-soft` exist).

## Principles the UI must keep

- A climb, not a score. Nothing calls a person's result a score; it is a rating against a target they agreed to. "Score" is
  reserved for the two company Scores (Compliance Score, Factory Score).
- The rule is the truth: every number is derived by `RuleEngine` / `KraEvaluator`; never round, colour or label a value in a
  way spec section 5 does not define. Month status concludes early only when rule 5.7 allows; the chip stays — (amber) otherwise.
- Colour is never the only signal: Met / — / Unmet always carry their text; a rating below 3 is red and the number itself is
  the signal; LOCKED says LOCKED.
- Plain verbs, sentence case; the only capitals are the system states SETUP and LOCKED and the codes LAST / SUM / AVE / MANUAL,
  RAMP / PRORATE / ZERO / FLAT / MANUAL. Dates `dd Mmm yyyy`; weeks `WWnn`; fiscal label beside the month ("Nov 2026 · 2H FY2026").
- `copper` is the single warm accent (one primary button per screen, the you-are-here marker, the current week). `cyan` means
  live data. `moss` / `amber` / `ember` are Met / — / Unmet with `-soft` grounds. No gradients, no shadows.
- Unbounded for headings and large numbers (tabular figures), Space Grotesk for everything else. Fonts are bundled under
  `wwwroot/fonts`; never load from Google Fonts or any CDN (the platform runs on a LAN without internet).
- One orchestrated animation on first load of Ascent; otherwise motion answers an action; `prefers-reduced-motion` turns it off.
- Keyboard focus ring (`focus` token, 2px, 2px offset) on every control; phone width works with no horizontal page scroll.

## Components (Razor partials in `Views/Shared`)

| Partial | Design component | Model |
| --- | --- | --- |
| `_StatusChip` | StatusChip (month status) | `(MonthStatus status, string? label)` |
| `_JudgementChip` | StatusChip (week judgement) | `(Judgement? judgement, string? label)` |
| `_Rating` | Rating (red below 3, amber "provisional") | `(decimal? value, int decimals, bool provisional)` |
| `_ModeBadge` | ModeBadge (SETUP / LOCKED · vN · approver · date) | `Kra` |
| `_MonthChip` | MonthChip (copper border on the current month) | `(month, fiscal, status, current, href)` |
| Week by week table (`.grid-table`, `.grid-cell`) | WeekGrid | built inline in `Views/WeekByWeek/Index.cshtml` |

Component classes (`.btn`, `.chip`, `.rating`, `.mode`, `.month-chip`, `.grid-cell`, `.card`, `.panel`, `.well`) live in the
`@layer components` block of `Styles/app.css`.
