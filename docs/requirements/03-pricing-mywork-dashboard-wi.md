# Requirements — Pricing, My Work, Dashboard, Work Instructions, Formulas, Photos (test docs 9–12 + doc 2 notes)

## Pricing ("Process System Pricing")
"+ Documents". Filters: Group, `Process System (Name or #)` (`#{Name} [{Group}] - {Number}`). Empty text
"Please select a Process System using the filters above.".
**Assumptions**: `Labor Rate ($/hr)`, `Mark-Up (%)`, `Premium Mark-Up (%)` (default 0; accept "020" = 20).
**Complexity / Surface Area** card: rows `One Sided Finishing`, `Two Sided Finishing`, `High Complexity`, each
[complexity %] [surface area sq ft].
Outputs (large orange, live): `Total Price` ($, 2 decimals), `Total Square Footage` (sum of the three areas).
Buttons Save (stores scenario on the schedule), Print. Formula: `Services/ScheduleCalculator.PriceAsync` — show a
"Pricing assumptions"/breakdown panel explaining material $/sq ft, labor $/sq ft, other $/sq ft and row costs.

## My Work
Tabs **My Work Progress** / **Processes Management**. Start bar: searchable dropdown of the group's schedules
`{Group}: {Schedule Name} (#{Number})` + "▶ Start Process" → creates an execution and opens it.
Grid (in-progress executions): `Order` (default sort, drag handle to reorder), `#`, `Group`, `Username`, `Date/Time`,
`Process Schedule Name`, Actions: "▶ Resume" (open), ✔ complete, ✖ cancel.
**Execution page** (`/my-work/:id`): title "{Schedule Name} (#{Number})"; toolbar "+ Adders [n]", "Internal DPM",
"AWFI Finishing Question Support", "Defects [n]", "View History". Body: one section per step "#1 Wide Belt Wood Sanding";
each line = red box (toggle) + description + grey value badge. Clicking the box records date/time + user (badge shows
count), turns into a check with timestamp. Values with min/max ranges can take a recorded value (flag out of range).
Defects: pick defect type + quantity (+notes). Adders: pick adder type + value. View History: all checks/defects/adders.
Complete / Cancel execution. If the group has "Enable Checklist Deletion on Submit", completing clears the checklist marks.
**Processes Management**: per schedule assign Department (drives Dashboard panels); manage Defect types (name + chart
colour) and Adder types for the group.

## Dashboard
Filters: Group, "Department Search". Tabs **My Work Processes**, **Devices**, **Department Management**.
Add KPI cards on top (processes running, completed this week, devices, defects this week) and charts (executions per day,
defects by type using the defect chart colours).
- **My Work Processes**: one panel per department "{Department} ({Group})" with "Process Search" and mini grid `#`,
  `Group`, `Last Run` (default desc), `Process Schedule Name`, `Actions` (Start/Resume).
- **Devices**: grid `#`, `Group`, `Device`, `Description`, `Actions` (Graphs, Edit, Delete); "+ Add Device".
  Modal "Add New Device": Group, Device Name, Description, Device Type (Camera, Scale(g), Temperature & Humidity Sensor,
  Scale(kg), Label Printer, Network Bridge, Dispense Machine), Network Bridge (devices of type Network Bridge in the group),
  Device IP Address "[e.g http://192.168.1.1 or https://192.168.1.1]" → "Device created.". Dispense Machine → modal
  "Canister Tint Assignment" with CANISTER 1…16 searchable material dropdowns. Graphs → line chart of the device metrics for a
  date. Devices post readings with their API key (`POST /api/devices/ingest` header `X-Api-Key`).
- **Department Management**: grid `#`, `Name`, `Group`, `Actions` (Edit, Delete); "+ Add Department" modal (Group,
  Department Name) → "Department created.". Duplicate names allowed.

## Work Instructions
List: Group, Search, "+ New Work Instruction". Grid `#` (document number), `Group`, `Name`, `Date/Time`, `Status`
("DRAFT v1"), Actions: View, Edit, Copy, Print, Delete (legacy delete was broken — must work).
Modal "Add New Work Instruction Document": Group, Document #, Document Name, Draft Status (read-only "DRAFT v1"); Back/Cancel.
**Document page** (view + edit mode, `/work-instructions/:id`, `?edit=1`): header "Standard Work Instruction", "#{Doc#} {Name}".
Sections (collapsible, "Document Specs" jump menu):
- Page 1: title "{Name} Process"; Document # | Issue Date | Revision #; Location; Controlled Copy / Uncontrolled Copy;
  **Edit Trail** (Version, Author, Date); **Related Documents**; **Department Approval Signatures** (N/A when empty).
- Page 2: **Approved Tools & Equipment**, **Materials** (pick from Equipment & Materials or free text).
- Page 3: steps — "Add New Step +", each step bar: Level badge + bold title, Edit/Copy/Delete, media thumbnails
  (image or video with ▶). Step modal "Edit Work Instruction": `Level`, `Description / Title`, file upload (drag & drop);
  allowed: jpg, jpeg, png, gif, mp4, mov — error "Invalid extension for file "TestingDoc.docx". Only "jpg, png, gif, mp4, mov"
  files are supported." (jpg and mp4 MUST work). "Select Slides to Print", "Print All Slides".
- Release: "Release" turns DRAFT vN into RELEASED vN; editing a released document starts DRAFT v(N+1); each change is added
  to the Edit Trail.

## Formulas (from doc 2 screenshots + POC)
List filters: Group, `Status` (Complete/Incomplete), search "Search by Formula Name, Formula Number or Customer Name".
Buttons "+ New Formulation", "Bulk Copy" (admins). Grid `#`, `Group`, `Category`, `Formula Name`, `Formula Number`,
`Customer Name`, `Status`, Actions (Edit, Copy, Print, Documents, Delete [admins]).
Editor: name, number, customer, category, batch size (g), container type/price, mark-up, substrate, notes, Spin/Spex ΔL Δa
Δb ΔE; ingredient grid (material picker from Base/Pigment/Dye/Product materials, grams, % of batch, lb/gal, cost); totals:
total weight, estimated gallons, material cost, formula price (cost × (1+markup) + container); VOC/HAP/TAP lb/gal of the
mix (weighted). Print = formula card / can label.

## Photo Gallery
Buttons "GO BACK", "+ NEW PHOTO". Filters "Filters by tags...", "Search by name...", "APPLY TAGS/SEARCH", "CLEAR FILTERS".
Grid of photo cards (image + caption + tags); edit (name, tags), delete, lightbox view. Upload jpg/png/gif.
