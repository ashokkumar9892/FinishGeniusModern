# Requirements — Groups, Users, Equipment & Materials (test docs 1, 2, 4)

Extracted from the AWFI test scripts (screenshots of the legacy ASP.NET MVC app at dev.finishgenius.net).
"(inferred)" = not directly visible. The new app keeps the behaviour but modernises the UI.

## Global conventions seen in the legacy app
- Toasts: green success ("Group created.", "Group deleted.", "User created.", "Vendor Created.", "Material created.",
  "API Key generated!"), red errors ("Duplicate Group Name", "Error Encountered.").
- Modal validation shows a red banner at the top of the modal ("Not a valid phone number") and red borders on invalid inputs.
- Grids: sortable, search, 50 per page, "Showing 1 to 50 of 95 entries (filtered from 2,226 total entries)",
  "No matching records found" / "No data available in table", multi-row selection by clicking rows (for bulk actions).

## 1. Groups
List columns: `#`, `Name` (default sort Name asc), `Is Default` ("Choose" button per row; the current user's default group
shows "☑ Default" and is highlighted), `Actions` (Edit, Copy, Delete). Top: Search, "+ New Group".

**Create New Group** modal: `Full Name` (required, unique among NON-deleted groups → "Duplicate Group Name"), `Logo Image`
(file, optional), `Enable Checklist Deletion on Submit` (checkbox). Save → "Group created.".

**Edit Group** modal: Full Name, Address1, City, Address2, State, Zip, Country, `Api Key` (read-only, placeholder
"API Key not generated", button "Generate API Key" → toast "API Key generated!" + GUID), `Time Zone` (Windows time zones,
default "(UTC-05:00) Eastern Time (US & Canada)"), Logo Image (+ preview), Enable Checklist Deletion on Submit.

**Delete**: modal "Delete Confirmation" — "Are you sure you want to delete the "Testing_02" group?" Yes/Cancel → "Group deleted.".

**Copy Group**: modal "Copy {Name} Group", heading "Copy Statistics", text "Copying this group will copy the following
items:" then a checklist with counts (Materials, Formulas, Process Steps, Process Schedules, Departments, My Work Processes,
Work Instructions, Photo Gallery, Equipment & Materials, Vendors), input `New Group Name`, buttons Copy/Cancel. Result toast
lists copied counts per item. NOT copied: dashboards/devices, started My Work executions, users, environmental data.
Preview counts must equal copied counts.

**Legacy defects to fix**: deleted group names must be reusable; groups created with "Enable Checklist Deletion" must be
visible; no page refresh needed after errors.

Non-admin users see only their own group(s) with Edit only (no New/Copy/Delete).

## 2. Users
List columns: `#`, `Group` (default sort), `Email`, `Username`, `First Name`, `Last Name`, `User Agreement Status`
(✔ green / ✖ red), `Status` (Enabled/Disabled), `Actions` (reset password ⟳ [System Admin], edit ✎, delete 🗑,
download ⬇ agreement). Filters: group + search. "+ New User".

**Create New User** modal: `Group` (default current), `Email Address`, `Username`, `New Password`, `Repeat Password`
(must match), `First Name`, `Last Name`, `Phone Number` mask "(718) 697 - 9892" → error "Not a valid phone number",
`User Roles` checkboxes: Finish Genius Pro, Finish Genius Pro+, Group Administrator, FG Support Agent, System Administrator.
Save → "User created.". Edit = same form (password optional).

Group Administrators manage users of their own group(s) only and cannot grant System Administrator / FG Support Agent.

**First login**: modal "Term & Conditions" with the agreement PDF, checkbox "By checking this box, I confirm that I have
read, understood, and agree to the terms and conditions outlined in the document provided", button "Accept". Sets
User Agreement Status ✔.

### Role / menu matrix
| Menu | FG Pro | FG Pro+ | Group Admin | Support Agent | System Admin |
|---|---|---|---|---|---|
| Groups | own (edit) | ✔ | ✔ | ✔ | full |
| Users | – | – | own group | – | all |
| Photo Gallery | ✔ | ✔ | ✔ | ✔ | ✔ |
| Equipment & Materials, Formulas, Process Steps, Process Schedules, Material Quantities, Pricing | ✔ | – | ✔ | – | ✔ |
| My Work, Dashboard | – | ✔ | ✔* | – | ✔ |
| Work Instructions | ✔ | ✔ | ✔* | – | ✔ |
| Import | – | – | ✔ | – | ✔ |
(*added for Group Admin in the new app.) Bulk Copy / Bulk Upload / Bulk Delete buttons: admins. Row delete & history: admins.

## 3. Equipment & Materials
Title "Equipment & Materials List". Toolbar: "Blk Cpy", "Blk Upld", "Blk Del", "Vndrs", "Locs", "Docs", "+ Item".
Tabs: Base Materials, Pigments, Dyes, Equipment, Sundry Items, Reorder Materials, Order History, Vendors, Reports.

Material grid columns: `#` (default desc), `Group`, `Material Category`, `Product Name`, `Product #`, `lb/Gal`, `$/Unit`,
`Material Type`, `VOC`, `HAP`, `TAP` (not on Equipment tab), `Total Inventory On Hand` (button "+/- Adjust 0.00"),
`Min. Quantity`, `Actions` (Documents, Edit, Delete, Copy, History ⟲). Rows selectable for bulk actions.

**Bulk Copy**: modal "Bulk Copying {n} Items", "Select the Destination Group", Copy ("Copying...") → modal
"Bulk Copy Results": "Successfully copied 50 materials to the Testing_17 group." (also for Vendors: "... 12 Vendors ...").
Must NOT show a spurious "Error Encountered." toast.

**Bulk Delete**: delete selected rows (with confirmation).

**Bulk Upload** modal "Bulk Material Excel Upload": "Browse File" (Excel), "Browse Documents" (optional related files),
"Download Sample", "Upload". Template = `testblikupld2.xlsx`:
- Sheets: `Base Materials`, `Pigment Materials`, `Dye Materials`, `Sundry Materials`, `Equipment` (names may have trailing
  spaces — trim). Sheet decides Material Type.
- Row 1 headers: `Material Category`, `Product Name`, `Product Rex #`, `Lb/Gal`, `Price Per Gallon/Pc.`, `VOC's`, `Hap's`,
  `TAP's`, `Document File`.
- Values may be text with trailing tabs and "$" — trim/parse. Category matched by name (create if missing). Document File
  matched to uploaded documents by file name.

**Vendors** tab columns: `#`, `Vendor Name`, `Payment Terms`, `Account #`, `Group Name`, `Contact Name`, `Office Phone`,
`Mobile Phone`, `Vendor Email`, `Requestor Email`, `Address`, `State`, `City`, `Zip`, `Actions` (Edit).
"Vendor Setup" modal: "Edit Existing Vendor (optional)" dropdown (fills form) or create: Group, Vendor Name, Address,
City, State, Zip, Payment Terms, Account #, Contact Name, Office Phone, Mobile Phone, Vendor Email, Requestor Email → "Vendor Created.".

**Locations** ("Locs"): modal "Select Material Type" → list of storage locations for that type; "+" add row, each row
name + delete + save. Used by inventory adjustments and the environmental report.

**Docs**: central document library modal (see Documents module): list `#`, `Group`, `Name`, `Thumbnail`, `Actions`
(Edit, Copy, Link, Delete); "+ New Document"; "Create & Edit Document" (Name, drag & drop file, Fullscreen View must work);
"Link" modal: "Please select the Objects you wish to link to this Document to." tree: Materials → Base, Dye, Pigment,
Sundry, Equipment; Formulations; Process Steps; Process Schedules. Linked docs show in each object's "Documents" action.

**Create New Material** ("+ Item"): `Group`, `Material Type` (Base, Dye, Equipment, Pigment, Sundry), `Material Category`
(searchable, de-duplicated), `Product Code`, `Product Name`, `Density (lb/gal)`, `Price ($/gal)`, `VOC (lb/gal)`,
`HAP (lb/gal)`, `TAP (lb/gal)`, `Min. Quantity` → "Material created.". Edit same form; Copy duplicates.

**Reorder Materials**: grid "Materials available:" (`Material Category`, `Product Name`, `Product #`, `lb/Gal`, `$/Unit`,
`Material Type`, `Total Inventory On Hand`, `Min. Quantity`, `Job to Order Qty`, `PO Qty` (editable), `PO Qty Type`
(editable)) + "Add to Order"; grid "Materials added to order:" + remove + "Place Order". "Create Order" modal: Vendor,
P.O. Number, Delivery Date, Ship To (Name default group name, Address1, City, Address2, State, Zip, Country default from
group) → "View Order" (preview) / "Place Order". Placed orders listed in **Order History**.

**Reports → Environmental Report**: date range; columns `Group`, `Category`, `Product Name`, `Product Number`, `Location`,
`Customer Name`, `Total Gallons Consumed`, `Total VOC's (lb) Emitted`, `Total HAP's (lb) Emitted`, `Total TAP's (lb) Emitted`;
"Export to PDF", "Export to Excel".

**Import page** (Group Admin): "Import Materials", "You can download a template file from here.", file input, "+ Import"
(same template as Bulk Upload).
