# Requirements — Sub Steps, Process Steps, Process Schedules, Material Quantities (test docs 6, 7, 8)

## Industry sectors
Fixed list: Aerospace, Automotive, Construction, Food, Heavy Manufacturing, Marine, Wood.

## Process Sub Step Setup (System Admin)
Title "Process Sub Step Setup". "+ New Sub Step". `Industry Sector` dropdown (reloads grid). Tabs **User** / **Admin**
(filter by User Role). Grid: `#`, `Step Sequence`, `Short Name`, `Name`, `Number of "pass throughs"`, Actions
(`Pull Downs`, `Edit`, delete).

Modal "Create New Sub Step": `User Role` (User/Admin, default User), `Name`, `Short Name` (shown on builder tiles),
`Sequence` (unique per industry sector → "Sequence already exists."), `Number of Pass Throughs` (≥1; N>1 gives tiles
4A, 4B…), `WebLink`, `Instruction` (textarea; shown in the builder "Instructions" box). Save → "Sub Step created.".

**Pull Downs** (per sub step): grid `#`, `Control Sequence`, `Choice Name`, `Header`, `Query`, Actions; modal
"Create New Pull Down": Choice Name, Header, Query, Sequence. In the new app "Query" is a safe structured source:
Material Type + category Filter1/Filter2 (categories matching become the dropdown options). When the user picks a category
in the step builder, that category's **characteristics** (defined in Material Categories) are rendered as inputs
(text / number with unit / material picker / yes-no / notes).

## Process Step List
Filters: Group (header), `Industry Sector`, Search. Buttons "Bulk Copy", "+ Create New". Grid: `#` (default desc),
`Group`, `Process Step Name`, `Industry Sector`, Actions (`Edit`, `Copy`, `Documents`, delete [admins]).
- Copy: modal "Copy Process Step {Name}" → `New Name` → new step in same group.
- Bulk Copy: select rows → modal "Bulk Copying 3 Items", "Select the Destination Group" → "Bulk Copy Results":
  "Successfully copied 3 steps to the Testing_18 group.".
- Delete with confirmation (legacy had none).

## Create / Edit Process Step (builder)
Title "Create Process Step". Top-right "+ Documents", "View Step" (enabled after first save).
Header: `Step Name`, `Group` (prefilled), `Industry Sector` (read-only after the first sub step is saved).
**Sub step carousel**: one tile per USER sub step of the sector ordered by Sequence, label = sequence (+A/B/C per pass),
Short Name on top; ‹ › arrows; selected tile orange. Admin sub steps are not in the carousel but appear in View Step.
Detail area: sub step Name heading + its pull-down inputs on the left; "Instructions" box (orange) with the sub step
Instruction on the right; "Save Sub-step" → toast "SubStep Saved" (first save creates the Process Step).
**View Step** modal "View Step ({Name})": every sub step pass in order "1 Name", "4A Name"… with "Sub Step Not Filled"
or the saved values. Button Close.

## Process Schedule List
Buttons "Bulk Copy", "+ Create New". Filters: Group, Search, label printer select ("{Printer} [{Group}]").
Grid: `#` (desc), `Group`, `Name`, `Number`, `Customer Name`, Actions: `Edit`, `Clone`, `Copy Master`, Print, History ⟲,
Archive 🗑, `Documents`, `Print Sample Label`.
- **Clone**: modal "Duplicate Process Schedule {Name}", warning "Note: A duplicate of this schedule will NOT include all
  edits made to associated steps.", `New Name`, `New Number` (prefilled with source number) → "Schedule Copied.".
- **Copy Master**: modal "Copy Process Schedule {Name}", note "Note: A copy of this schedule will include all edits made to
  associated steps.", New Name/New Number → "Schedule Copied.".
- **Bulk Copy**: "Bulk Copying 1 Items" → "Successfully copied 1 schedule to the Testing_17 group.".
- Print (printable schedule), History (audit), Archive (soft delete), Print Sample Label (label print).

## Create / Edit Process Schedule
Title "Create Process Schedule" / "Edit Process Schedule #41413". Add a **Back** button (tester request).
Fields: `Group`, `Schedule Name`, `Schedule #`, `Customer Name`. Dual list: **Available Steps** (search, checkboxes,
select-all, paging) ⇄ buttons "add ›" / "‹ remove" ⇄ **Assigned Steps** (search, checkboxes, 👁 view, ✎ edit, ☰ drag to
reorder; same step may be added more than once). Save (create redirects to edit). Edit page also has Print.
Assigned step ✎ → modal "Edit Step ({Name})": `Name` (schedule-level rename), "Edit Input Range" (per-value overrides:
value + min/max), list of sub step passes with values / "Sub Step Not Filled". These are the "edits made to associated
steps" kept by Copy Master and dropped by Clone.

## Material Quantities
Title "Material Quantities" + "+ Documents". Filters: Group, `Process System (Name or #)` — options
`#{Schedule Name} [{Group}] - {Number}`. Empty text "Please select a Process System using the filters above.".
Inputs: `One Sided Finishing (sq ft)`, `Two Sided Finishing (sq ft)` (default 0). Outputs (large orange):
`Square Footage` = one + 2 × two; `Production Time (Hrs)` (2 decimals). Table "Material Quantities": `Name`,
`Quantity` ("0.064 Gallons", 3 decimals). Live recalculation. Buttons Print, Save (stores the estimate on the schedule).
Calculation implemented in `Services/ScheduleCalculator.cs` (coverage, mix %, production rate characteristics).
