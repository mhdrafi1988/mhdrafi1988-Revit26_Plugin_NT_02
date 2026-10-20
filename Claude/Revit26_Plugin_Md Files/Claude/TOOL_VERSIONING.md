# Tool Versioning

What a `_V0xx` folder suffix actually means in this repo, the states a
version can be in, and how retirement actually works — reverse-engineered
from the `.csproj` and ribbon files, since none of this is written down
anywhere else. Folder/dependency layout is in `CLAUDE.md` and
`Claude/ARCHITECTURE.md`; this file is specifically about the version
lifecycle.

## It's not semver, and it's not global

`V0xx` is a per-tool-lineage folder suffix (`AutoSlopeByDrain_V007` →
`_V010`), unrelated to any assembly/NuGet version — there is no
`AssemblyVersion` in this project at all (see `CLAUDE.md`). Numbering is
**not consistently zero-padded**: the same family appears as both
`RoofRidgeLines_V057_By_POints` (3-digit) and `RoofRidgeLines_V57`/`V56`/
`V60` (bare) elsewhere. Don't assume you can sort folders lexicographically
to find the newest — read the ribbon file's grouping/comments instead (see
below), or the tooltip text, which is the one place version identity is
guaranteed human-readable.

## A higher version is not always a replacement for a lower one

Several pulldowns deliberately keep **multiple coexisting implementations**
side by side rather than one superseding chain. From
`RoofToolsRibbon.cs`'s own comment on the "By Point" pulldown:

> Slope — By Point (3 coexisting implementations) and By Drain (2) each
> collected under one pulldown button... Only versions whose source still
> exists are listed here.

`Btn_AutoSlopeByPoint_028`, `Btn_AutoSlopeByPointRidge_001`, and
`Btn_AutoSlopeByPoint_MultiCopies28` all live in the same dropdown as
genuinely different algorithms (base, ridge-aware, multi-copy), not three
generations of the same one. Before assuming the highest `V0xx` is "the"
current tool and the rest are dead weight, check whether the ribbon file
groups them as alternates.

## The states a version folder can actually be in

1. **Live** — its Command class is referenced by fully-qualified name in a
   `Menu/00_Push_Button_Menu_items/Ribbon/*Ribbon.cs` file (or
   `QuickAcces.cs`). This is a small minority: as of this writing, ~38
   distinct Command classes are wired up against 225 `_V0xx`-suffixed
   folders under `Menu/` — most version folders are not reachable from the UI.
2. **Compiled but orphaned** — this is an SDK-style project, so every
   `.cs` file under the project root is compiled by default unless
   explicitly excluded. A folder with no ribbon reference still builds into
   the assembly; it just has no button pointing at it. This is the state
   most non-live version folders are actually in — "not live" does not
   mean "not compiled." Check the `*Ribbon.cs` files, not the `.csproj`,
   to know if a tool is reachable.
3. **Explicitly retired** — excluded from the build entirely via a
   `<Compile Remove="Menu\...\FolderName\**" />` block in
   `Revit26_Plugin.csproj`. This is the actual mechanism used to kill off
   abandoned early drafts (e.g. the repo's `CreasetoLines\Creaser_V01`
   through `Creaser_V0302a` draft sequence is retired this way — a dozen-plus
   `**` exclusions in a row). Source stays on disk for reference; it simply
   never compiles.
4. **Never entered the build** — a handful of folders contain only a `.zip`
   with no extracted `.cs` source (see `Claude/RIBBON_AND_ICONS.md`'s
   sibling note in `Ribbon_Code_Notes.md` §5). These aren't excluded from
   anything — there's nothing for the compiler to see yet.

Individual stray files are also sometimes excluded the same way
(`<Compile Remove="App-backup.cs" />`, a few one-off `.cs` files inside
otherwise-live folders) — grep the `.csproj` for `Compile Remove` before
assuming a `.cs` file you're editing is actually part of the build.

## Retiring a version — how to do it consistent with the repo

Don't delete the folder. Either:
- Leave it as compiled-but-orphaned (the default/most common outcome —
  nothing extra to do once you stop referencing it from a `*Ribbon.cs`
  file), or
- If it doesn't compile, or you want it excluded outright, add a
  `<Compile Remove="Menu\<path>\<FolderName>\**" />` line grouped with the
  other exclusions in the `.csproj`.

Informal in-repo signals for "don't touch, this is going away": a filename
prefixed `Tobe deleted...` (seen once, already excluded via `Compile
Remove`), and a `-backup` suffix on a file kept alongside the live one
(`App-backup.cs`, also excluded).

## When does a change warrant a new `_V0xx` folder vs. an in-place edit?

Not written down anywhere, but the pattern across git history and the audit
is: bug fixes, error-handling hardening, and refactors that preserve
behavior happen **in place** in the current version's files (e.g. the
try/catch and structure fixes in `Claude/REVIT_PLUGIN_AUDIT.md` were applied
to existing files, not new version folders). A **new folder** appears when
the tool gains different behavior worth being able to fall back from — e.g.
`AutoSlopeByDrain_V010`'s multi-roof support, called out in its own
`Commands/AutoSlopeDrainCommand.cs` header as a deliberate, confirmed
decision (`NEW (V008), per Rafi's confirmed multi-roof decision
(2026-09-08)`). If a change is a confirmed, real behavior change rather than
a fix, prefer a new version folder and update the ribbon wiring, matching
this pattern — but treat this section as inferred convention, not a
documented rule, and confirm with the maintainer if a specific case is
ambiguous.
