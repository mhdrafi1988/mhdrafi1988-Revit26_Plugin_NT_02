# Tag To Text — V001

Copies tags from the active view into **text notes with leaders (arrows)**, at the same position and
with the same text. The text can go in the **same view** or in a **new duplicate of the view**.
UI mock-up: [`UI_Mockup.html`](UI_Mockup.html) (open it in a browser).

| Item | Value |
|---|---|
| Tool name | Tag To Text, v1.0.1 (plugin 6.14.1) |
| Folder | `Menu/Tagging/TagToText_V001/` |
| Namespace | `Revit26_Plugin_Revised.TagToText.V001` |
| Command | `Revit26_Plugin_Revised.TagToText.V001.Commands.TagToTextCommand` (via `ToolGuard.RunCommand`) |
| Ribbon | **SloperPro-Documents › Tagging › Tag To Text** (`Btn_TagToText_V001`) |
| Catalog | `ToolCatalog.TagToText` |
| Settings | `%AppData%\Revit26_Plugin_Revised\TagToText\settings.json` (with a `Version` field) |
| Tests | `dotnet test Tests/TagToText.Tests` |

## Code layout

| File | Job |
|---|---|
| `Commands/TagToTextCommand.cs` | Checks the document and view, opens the window |
| `Core/Services/TagReader.cs` | Finds tags; reads text, text position, leader end and bend |
| `Core/Services/TagToTextService.cs` | The run: duplicate view, create notes, hide/delete originals, progress, cancel |
| `Core/Services/TextRules.cs` | Revit-free rules: line splitting, prefixes, unique view names (unit-tested) |
| `Core/Models/*` | Enums, the settings file, what was read from a tag |
| `Infrastructure/TagToTextHandler.cs` | External event handler (via `ToolGuard.RunHandler`) |
| `UI/*` | Window (shared `ToolWindowShell`) and view model |

## Tags it copies

| Tag type | Text read | Text position / leader |
|---|---|---|
| Spot Elevation | `ValueString`; when Revit returns it empty, "Single/Upper Value" (+ "Lower Value") in the type's units. Indicator, prefix and suffix added if missing | Text position; leader to the spotted point, bend at the shoulder |
| Spot Coordinate | `ValueString` lines; when empty, "Top Coordinate" / "Bottom Coordinate". N/S and E/W indicators, prefixes and suffixes added if missing | **Two notes**: N at the text position, E one line below; both lead to the spotted point |
| Spot Slope | `ValueString` | Just above the slope arrow, leader to the spotted point (the API gives no text position) |
| Element tags | `TagText` | Tag head; free leader end, or the tagged element's location for attached leaders (linked elements too) |
| Room / Area / Space tags | `TagText` | Tag head, leader end and bend |

Every text can be edited in the table before it is written. A tag with no readable text shows "No text read · double-click Text to type it"; typing text ticks it. Supported views: plan, ceiling, structural,
area, section, elevation, detail and drafting.

## UI

1. **Source**: selected tags / all tags in the active view, **Pick Tags…**, **Refresh List**, a tick box per tag type.
2. **Table**: tick, type, text (editable), leader yes/no, status ("OK", "owned by X", "in a group", problems).
3. **Text Output**: text type, prefix, suffix, keep leader/arrow, match tag rotation, source note in Comments.
4. **Place Text In**: same view, or a new view duplicated **without** or **with detailing**, view name,
   open the new view when done.
5. **Original Tags** (same view only; greyed out for a new view): Keep / Hide in view / Delete.
6. Warning that the text is static.
7. **Progress**: bar, `n of N · %`, step, Created / Skipped / Failed.
8. Run log with Copy Log and Open Log Folder. Footer: **Create Text**, **Cancel**, **Close**.

All options are remembered and restored when the window opens again.

## Behaviour and robustness

- One `TransactionGroup` per run. Each tag is created in its own `SubTransaction`: a tag that fails is
  logged and skipped, the rest go ahead. **Cancel**, or an unexpected error, rolls back everything,
  including the duplicated view.
- **Without detailing** uses `ViewDuplicateOption.Duplicate`, **with detailing**
  `ViewDuplicateOption.WithDetailing`, after `View.CanViewBeDuplicated`.
- With **Delete**, tags in groups or owned by another user (`WorksharingUtils.GetCheckoutStatus`) are
  kept and logged. With **Hide**, tags that can't be hidden in the view stay visible.
- If Revit refuses a leader, the note is kept without it and a warning is logged.
- The window can't close while a run is in progress. Unexpected errors go to
  `%AppData%\Revit26_Plugin_Revised\Logs` through `ToolGuard.Report`.

## Needs checking in Revit

Built and unit-tested on Linux; not yet run in Revit. Check these on a real model:

1. Spot Coordinate `ValueString` gives two lines (N and E). If it gives one, the tool makes one note.
2. Spot prefixes/indicators are not doubled (the tool only adds them when missing).
3. Text sits centred on the tag head and the arrowhead matches the chosen text type.
4. Progress updates and Cancel respond during a long run.
