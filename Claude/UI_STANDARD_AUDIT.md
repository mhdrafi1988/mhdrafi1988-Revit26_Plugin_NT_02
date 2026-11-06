# UI Standard Audit — spacing, padding, typography (2026-11-06)

Scope: every `*.xaml` under `Menu/` (191 files, ~169 windows) plus `Shared/SharedStyles.xaml` and
`Shared/Controls/ToolWindowShell*`. Counts come from a script over the whole tree, not a sample.
No tool code was changed by this audit.

## 1. What exists today

| Area | Finding |
|---|---|
| Shared shell | `ToolWindowShell` (header / one scroll body / footer) is used by 128 of ~169 windows. Its own spacing is already consistent: body `12,8`, footer `12,8`, header chips `12,8,12,0`. |
| Shared styles | `SharedStyles.xaml` has 1,236 lines of colours, card, button, text, grid styles. It has **no spacing, padding, radius or font-size tokens**, so every window hard-codes its own numbers. |
| Windows not on the shell | ~41, mostly `RoofTools/Points`, `ViewTools/Create`, `Manage/Links`, `Manage/Worksets`, `Sheet`, `Tagging`, `ScheduleExportImport`, `CreaserAdv`, `CompareRoofs`. These are the main source of drift. |

## 2. Inconsistency measured

| Item | Result |
|---|---|
| Distinct `Margin` values | **357** (top: `0,0,6,0` ×693, `0,0,0,8` ×496, `0,0,8,0` ×438, `0,0,0,6` ×418 …) |
| Distinct `Padding` values | **91** (`10,8` ×497, `10,6` ×108, `10,4`, `6,2`, `8,6`, `9,7`, `12,7`, `7,2` …) |
| Spacing numbers on a 4 px grid | **77 %** of 30,897. Off-grid: `6` ×2,374, `10` ×2,090, `2` ×643, `3` ×616, `14` ×334, `7` ×252, `5`, `9`, `2.5`, `22` |
| Font sizes in use | 8, 9, 9.5, 10, 10.5, 11, 11.5, 12, 12.5, 13, 14, 15, 18, 19, 20 (**15 different sizes**; 236 uses are below 10 pt, hard to read) |
| Corner radius | 4, 5, 6, 8, 9, 10, plus `10,10,0,0` and `8,8,0,0` (7 variants) |
| Font family | `Consolas` hard-coded 346×, `Segoe UI` 5×, `{DynamicResource SystemFont}` only 28× |
| Hard-coded colours | 108 `#RRGGBB` values bypass the colour tokens |
| Button padding (explicit) | 13 variants (`8,0` `10,4` `9,0` `6,0` `0` …); control heights 20, 22, 26, 28, 30, 32, 36, 38, 46 px |
| TextBox padding | 12 variants (`6,2` `2` `4,2` `6,0` `6,4` …); heights 24, 26, 30 |
| Style coverage | Button 92 % styled, TextBox 91 %, **ComboBox 55 %**, **DataGrid 42 %**, **CheckBox 16 %** |
| Worst files (off-grid values) | `AutoSlopeByDrain_V007/V011/V012`, `CombinedRoofTools_V002/V003 AutoSlopeByDrainTab`, `TrackerId_V002`, `SectionFromLineDialog`, `LinkedDetailLineGenerator_VA009/VA010`, `ViewAutoRenamerWindow` |
| Duplicate versions | ~30 tools still have two or more `_V` folders side by side (e.g. `LinkedDetailLineGenerator_VA009/VA010`, `TrackerId_V001/V002`). They double the cleanup and the testing. |

## 3. Professional standard to adopt

**Spacing scale (4 px grid, no other values):** `4` · `8` · `12` · `16` · `24`. Allowed micro-gap: `2`.

| Token | Value | Use |
|---|---|---|
| `Space.XS` | 4 | gap between icon and label, inside chips |
| `Space.S` | 8 | gap between controls in a row, between label and field |
| `Space.M` | 12 | window body padding, card padding, gap between rows |
| `Space.L` | 16 | gap between sections/cards |
| `Space.XL` | 24 | dialog outer padding (small dialogs without shell) |

| Element | Standard |
|---|---|
| Window body / footer | `12,8` (already used by the shell) |
| Card / group box | padding `12`, gap between cards `12` |
| Label above field | bottom margin `4`; field to next field `8` |
| Buttons | height **32**, padding `12,0` (compact: 24 high, `8,0`); gap between buttons `8` |
| TextBox / ComboBox | height **28**, padding `8,4` (text never touches the border) |
| DataGrid | row height 28, cell padding `8,4`, header padding `8,6` |
| CheckBox / Radio | vertical margin `4`, text offset `6` |
| Corner radius | 4 (inputs, buttons) · 8 (cards) · 12 (pills); nothing else |

**Typography (7 sizes max):** 10 hint/caption · 11 secondary · 12 body (default) · 13 field value ·
14 section title · 18 window title · 24 metric number. Nothing below 10. Monospace only for log and
code text, through one `MonoFont` token.

**Window behaviour a professional tool needs:** one shell for every window; `MinWidth`/`MinHeight` set;
`CanResizeWithGrip` for anything with a grid or log; `CenterOwner` for tool windows (Revit main window as
owner); `IsDefault`/`IsCancel` on the primary and close buttons (only 46 are set today); tab order set
(4 `TabIndex` uses in the whole repo); `AutomationProperties.Name` on inputs (0 today); tooltips on
buttons (already strong, 1,370); colours and fonts only from tokens.

## 4. Gaps to close (in priority order)

1. **Add the tokens** to `SharedStyles.xaml` (`Space.*` thickness, `Radius.*`, `FontSize.*`, `MonoFont`,
   `ControlHeight*`) and implicit styles for `ComboBox`, `CheckBox`, `DataGrid` so unstyled controls pick
   up the standard automatically. This is additive and changes nothing by itself.
2. **Move the ~41 non-shell windows onto `ToolWindowShell`.**
3. **Snap off-grid values** (`6→8`, `10→8/12`, `3/5/7/9→4/8`, `14→12/16`) and font sizes to the scale.
4. **Replace the 108 hard-coded colours and 346 `Consolas` uses** with tokens.
5. **Add `IsDefault`/`IsCancel`, `TabIndex`, `AutomationProperties.Name`** to every window.
6. **Retire superseded duplicate version folders** (git already keeps history).

## 5. Cost and rule impact

Steps 2–5 touch almost every tool window. Under `CLAUDE.md` each touched tool then needs a version bump
in `ToolCatalog.cs`, a `CHANGELOG.md` entry and an updated HTML preview in `Claude/Previews/`. That is
roughly 100+ tools. Snapping values will move controls by 1–2 px, so the result has to be checked on
screen in Revit, which can't be done from this Linux session (the project builds here, the windows
can't be run).

## 6. Suggested rollout

1. Tokens + implicit styles (no visual change). One PR.
2. Convert one group at a time (e.g. RoofTools/Points → ViewTools → Manage → Sheet …), each with a
   build, version bump, changelog and preview, so each group can be checked in Revit before the next.

## 7. Rollout status (2026-11-06)

Tokens added to `Shared/SharedStyles.xaml` (6.39.0). Margins, paddings and font sizes snapped to the
standard on the current version of every wired tool in: Roof Points (6.39.0), View Tools (6.39.1), Manage
(6.39.2), Sheet (6.39.3), Floor Tools (6.39.4), Roof Tools other than Points (6.39.5), Detail Lines and
Dimensions (6.39.6), Setup (6.39.7), Tagging (6.39.8).

Not done yet: older coexisting version folders; Workset Creator V017 and the WIP tools (not wired);
moving the non-shell windows (e.g. Link Version Checker V007) onto `ToolWindowShell`; implicit styles for
ComboBox / CheckBox / DataGrid; button and input heights; replacing hard-coded colours and `Consolas`
with tokens; `IsDefault` / `IsCancel`, `TabIndex`, `AutomationProperties.Name`. None of this was built or
opened in Revit from the Linux session; check the windows in Revit first.
