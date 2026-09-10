# Finish Genius — developer conventions

Read this before adding a module. The goal is one consistent app: every screen looks and behaves the same.

## Layout

```
backend/FinishGenius.Api        ASP.NET Core 10 Web API (+ serves the built SPA from wwwroot)
  Domain/                       EF entities (all tables in SQL schema "fg")
  Data/AppDbContext.cs          DbContext, Data/Migrations, Data/DbSeeder.cs
  Infrastructure/               CurrentUser (group access), Access (role lists), FileStorage, AuditService, ApiException
  Services/                     ScheduleCalculator (quantities/pricing/checklists), GroupCopyService (copy group / bulk copy)
  Controllers/                  one controller per module
frontend/                       React 19 + Vite + TypeScript + Tailwind 3 + react-query + recharts + lucide-react
  src/lib/                      api (axios), auth (useAuth/useMe/useGroup/useLookups), access (role matrix), format, types
  src/components/               ui (PageHeader, Card, Tabs, Modal, ConfirmDialog, Field, SearchInput, Checkbox, EmptyState, Note…),
                                DataTable, SearchSelect, FileDrop, HistoryModal, EntityDocuments, toast (useToast)
  src/pages/<module>/           pages; routes are already declared in src/App.tsx
docs/                           documentation
```

## Backend rules

- Controller: `[ApiController] [Authorize(Roles = Access.X)] [Route("api/<kebab-module>")]`, primary-constructor injection of
  `AppDbContext db, CurrentUser me, AuditService audit, FileStorage files` as needed.
- **Tenant scoping (critical):** everything belongs to a Group.
  - List endpoints take `?groupId=` and call `await me.EnsureGroupAsync(groupId)`.
  - Get/update/delete by id: load the entity, then `await me.EnsureGroupAsync(entity.GroupId)`; 404 if missing/deleted.
  - Moving/copying to another group: also `EnsureGroupAsync(destinationGroupId)`.
- Return DTOs (anonymous objects / records), never entities with navigation properties. JSON is camelCase, enums are numbers,
  dates are UTC (`DateTime.UtcNow`).
- Validation / errors: `throw ApiException.Bad("Human readable message.")`, `throw ApiException.NotFound("Material")`.
  Middleware turns them into `{ "message": "..." }` with the right status code. Use the exact legacy wording where the test
  documents quote it (e.g. "Seqaunce already exists." is a legacy typo → use "Sequence already exists.").
- Success: `return Ok(new { message = "Material created.", id = entity.Id })` — the UI shows `message` in a green toast.
- Soft delete where the entity has `IsDeleted` / `IsArchived`. Uniqueness checks must ignore soft-deleted rows.
- Audit every create / update / delete / copy: `audit.Log("Material", id, "Updated", "Price 12.00 → 14.50", groupId)`
  (added to the change tracker, saved with your `SaveChangesAsync`). `GET /api/history?entityType=&entityId=` shows it.
- Files: `await files.SaveAsync(formFile, $"photos/{groupId}", FileStorage.ImageExtensions)` returns a relative path; the
  browser loads it through `GET /api/files/{path}` (`fileUrl(path)` in the frontend). Folder must be `<kind>/<groupId>`.
- Uploads use `[FromForm]` + `IFormFile`. Excel via ClosedXML.
- Heavy multi-row operations: use a transaction via `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`
  (retry-on-failure is enabled, so plain `BeginTransaction` must be wrapped in the strategy).

## Frontend rules

- Data: `useQuery({ queryKey: ['materials', groupId, type], queryFn: () => api.get('/materials', { params: { groupId, type } }).then(r => r.data) })`
  and `useMutation` + `qc.invalidateQueries(...)`. Show `toast.success(res.data.message)` / `toast.error(errorMessage(e))`.
- Current group: `const { groupId, groups } = useGroup()` (selected in the header; every list is scoped to it). Bulk-copy
  destination pickers use `groups` (all groups the user may access).
- Current user / roles: `useMe()`, `isAdmin(me)`, `isSystemAdmin(me)`, `hasRole(me, Roles.X)` from `@/lib/access`.
- Page skeleton: `<PageHeader title breadcrumbs={['Module']} actions={<button className="btn-primary">…</button>} />`, then
  content. Lists use `<DataTable>` (sorting, search, 50/page, selection, "Showing X to Y of Z entries").
- Forms in `<Modal footer={<><button className="btn-secondary">Cancel</button><button className="btn-primary">Save</button></>}>`,
  fields in `<Field label required error>` with `className="input"` inputs; show server validation with `<ErrorBanner>`.
- Deletes always go through `<ConfirmDialog message='Are you sure you want to delete the "X" group?'>`.
- Dropdowns with many options: `<SearchSelect options={[{value,label,sub}]} …/>`.
- Buttons: `btn-primary`, `btn-secondary`, `btn-ghost`, `btn-danger`, `btn-success`, `btn-sm`, `btn-icon`; icons from `lucide-react`
  at `h-4 w-4`. Row actions are small ghost/icon buttons with `title` tooltips.
- Print views: the Layout hides sidebar/header when printing; add `no-print` to controls and use `window.print()`.
- Must work at phone width (tables scroll horizontally, use `hideBelow` on secondary columns).
- Use `@/` imports. No `any` unless unavoidable. `npx tsc -b` must pass with zero errors.
