# Tag To Text — V001 (planned)

> Status: **design / spec only.** No code yet. UI mock-up: [`UI_Mockup.html`](UI_Mockup.html)
> (open it in a browser).

Copies tags from the active view into **text notes with leaders (arrows)**, at the same
position and with the same text. The text can go in the **same view** or in a **new
duplicate of the view**.

| Item | Value |
|---|---|
| Tool name | Tag To Text |
| Folder | `Menu/Tagging/TagToText_V001/` (`Commands`, `Core`, `UI`, `Infrastructure`) |
| Namespace | `Revit26_Plugin_Revised.TagToText.V001` |
| Command | `Revit26_Plugin_Revised.TagToText.V001.Commands.TagToTextCommand` (via `ToolGuard.RunCommand`) |
| Catalog | `ToolCatalog.TagToText = Add("Tag To Text", "1.0.0", "V001", …)` |
| Settings | `%AppData%\Revit26_Plugin_Revised\TagToText\settings.json` (with a version field) |
| Ribbon panel | _to be decided_ |

## Tags it copies

| Tag type | Text read | Leader read |
|---|---|---|
| Spot Elevation | Displayed value, with prefix and suffix | Leader end, bend, text position |
| Spot Coordinate | N / E lines | Same as above |
| Spot Slope | Value, e.g. `2.00%` | Arrow point and direction |
| Element tags (roof, door, keynote, multi-category…) | `TagText` | Tag head, leader end, bend |
| Room / Area / Space tags | `TagText` | Tag head, leader end |
| Dimensions | Not in V001 | — |

Output is a `TextNote` with a leader. The arrowhead comes from the chosen Text Type.
Supported views: plan, section, elevation, detail and drafting. Other views are refused
with a message.

## UI

1. **Source**: selected tags / all tags in the active view, a **Pick Tags…** button, and a
   tick box for each tag type.
2. **Results table**: tick, type, text, leader yes/no, status (e.g. "Owned by user X").
3. **Text Output**: text type, prefix, suffix, keep leader/arrow, match tag rotation,
   add a source note (tag Id + date).
4. **Original Tags**: Keep / Hide in view / Delete.
5. **Place Text In**:
   - Same view (current)
   - New view (duplicate of the current view), with:
     - **Duplicate**: Without detailing / With detailing
     - **View name** (default: `<current view name> – Text`, made unique if taken)
     - **Open the new view when done**
6. **Warning**: the text is static and won't update with the model.
7. **Progress**: progress bar, `n of N · %`, current step, and Created / Skipped / Failed
   counts.
8. **Buttons**: Preview, Open log, Cancel, Create Text, Close.

## Behaviour and robustness

- All changes run in one `TransactionGroup`. An error rolls back everything, including
  the duplicated view.
- **Duplicate without detailing** uses `ViewDuplicateOption.Duplicate`; **with detailing**
  uses `ViewDuplicateOption.WithDetailing`. Check `View.CanViewBeDuplicated` first.
- Tags owned by another user (`WorksharingUtils.GetCheckoutStatus`), tags in groups, and
  tags with no readable text are skipped, and the reason is shown in the table.
- Cancel stops after the current tag and rolls back.
- Unexpected errors go to `%AppData%\Revit26_Plugin_Revised\Logs` through `ToolGuard.Report`.

## Open questions

1. Which ribbon panel gets the button?
2. Spot Coordinate: one text note with 2 lines, or two notes?
3. With **New view**, should "Hide/Delete original tags" apply only to the current view?
4. Should every option shown be in V001, or should some wait for a later version?
