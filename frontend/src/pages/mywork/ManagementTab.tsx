import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, ArchiveRestore, Check, Pencil, Plus, Trash2, X } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { Card, Checkbox, ConfirmDialog, EmptyState, ErrorBanner, LoadingBlock, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { ColorDot, type AdderType, type ApiMessage, type DefectType, type DepartmentRow, type ProcessRow } from './shared'

export function ManagementTab() {
  return (
    <div className="space-y-4">
      <ProcessesCard />
      <div className="grid gap-4 lg:grid-cols-2">
        <TypeManager kind="defect" />
        <TypeManager kind="adder" />
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Department assignment per schedule
// ---------------------------------------------------------------------------

function ProcessesCard() {
  const { groupId } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const [saving, setSaving] = useState<number | null>(null)

  const processes = useQuery({
    queryKey: ['my-work-processes', groupId],
    queryFn: () => api.get<ProcessRow[]>('/my-work/processes', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
  const departments = useQuery({
    queryKey: ['departments', groupId, ''],
    queryFn: () => api.get<DepartmentRow[]>('/departments', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })

  const assign = useMutation({
    mutationFn: ({ scheduleId, departmentId }: { scheduleId: number; departmentId: number | null }) =>
      api.put<ApiMessage>(`/my-work/processes/${scheduleId}/department`, { departmentId }).then((r) => r.data),
    onMutate: ({ scheduleId }) => setSaving(scheduleId),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['my-work-processes'] })
      qc.invalidateQueries({ queryKey: ['departments'] })
      qc.invalidateQueries({ queryKey: ['dashboard-departments'] })
      qc.invalidateQueries({ queryKey: ['process-schedules'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: () => setSaving(null),
  })

  const columns: Column<ProcessRow>[] = [
    { key: 'id', header: '#', className: 'w-16' },
    {
      key: 'name',
      header: 'Process Schedule Name',
      cell: (r) => (
        <div>
          <div className="font-medium">{r.name}</div>
          <div className="text-xs text-muted-foreground">
            #{r.number}
            {r.customerName ? ` · ${r.customerName}` : ''}
          </div>
        </div>
      ),
    },
    {
      key: 'departmentName',
      header: 'Department',
      sortValue: (r) => r.departmentName ?? '',
      cell: (r) => (
        <div className="flex items-center gap-2">
          <select
            className="input h-9 min-w-[180px]"
            aria-label={`Department for ${r.name}`}
            value={r.departmentId ?? ''}
            disabled={saving === r.id}
            onChange={(e) => assign.mutate({ scheduleId: r.id, departmentId: e.target.value ? Number(e.target.value) : null })}
          >
            <option value="">— Unassigned —</option>
            {(departments.data ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
          {saving === r.id && <Spinner className="text-muted-foreground" />}
        </div>
      ),
    },
    { key: 'stepCount', header: 'Steps', align: 'right', hideBelow: 'md' },
    { key: 'runs', header: 'Runs', align: 'right', hideBelow: 'sm', cell: (r) => (
      <span className="tabular-nums">
        {r.runs}
        {r.activeRuns > 0 && <span className="ml-1.5 badge bg-primary/10 text-primary">{r.activeRuns} active</span>}
      </span>
    ) },
    { key: 'lastRun', header: 'Last Run', hideBelow: 'md', sortValue: (r) => r.lastRun ?? '', cell: (r) => (r.lastRun ? dateTime(r.lastRun) : <span className="text-muted-foreground">Never</span>) },
  ]

  return (
    <Card title="Process Schedules" bodyClassName="p-0">
      {processes.isError && <div className="p-3"><ErrorBanner message={errorMessage(processes.error)} /></div>}
      <div className="px-4 pt-3 text-xs text-muted-foreground">
        Assign each process schedule to a department — departments drive the panels on the Dashboard. Changes save immediately.
      </div>
      <div className="p-3">
        <DataTable
          bare
          rows={processes.data ?? []}
          columns={columns}
          rowKey={(r) => r.id}
          loading={processes.isLoading}
          initialSort={{ key: 'name', dir: 'asc' }}
          searchPlaceholder="Search process schedules…"
          emptyTitle="No process schedules in this group"
        />
      </div>
    </Card>
  )
}

// ---------------------------------------------------------------------------
// Defect types / adder types
// ---------------------------------------------------------------------------

type TypeRow = (DefectType | AdderType) & { chartColor?: string | null }

const DEFAULT_COLORS = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948']

function TypeManager({ kind }: { kind: 'defect' | 'adder' }) {
  const { groupId } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const isDefect = kind === 'defect'
  const path = isDefect ? '/my-work/defect-types' : '/my-work/adder-types'
  const noun = isDefect ? 'defect type' : 'adder type'

  const [showArchived, setShowArchived] = useState(false)
  const [name, setName] = useState('')
  const [color, setColor] = useState(DEFAULT_COLORS[0])
  const [formError, setFormError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ id: number; name: string; color: string } | null>(null)
  const [deleting, setDeleting] = useState<TypeRow | null>(null)

  const list = useQuery({
    queryKey: [isDefect ? 'defect-types' : 'adder-types', groupId, showArchived],
    queryFn: () => api.get<TypeRow[]>(path, { params: { groupId, includeArchived: showArchived } }).then((r) => r.data),
    enabled: groupId > 0,
  })

  const refresh = () => qc.invalidateQueries({ queryKey: [isDefect ? 'defect-types' : 'adder-types'] })

  const create = useMutation({
    mutationFn: () => api.post<ApiMessage>(path, { groupId, name, chartColor: isDefect ? color : undefined }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setName('')
      setFormError(null)
      const used = new Set((list.data ?? []).map((t) => t.chartColor))
      setColor(DEFAULT_COLORS.find((c) => !used.has(c) && c !== color) ?? DEFAULT_COLORS[0])
      refresh()
    },
    onError: (e) => setFormError(errorMessage(e)),
  })

  const update = useMutation({
    mutationFn: (p: { row: TypeRow; name: string; color?: string | null; isArchived?: boolean }) =>
      api.put<ApiMessage>(`${path}/${p.row.id}`, {
        groupId,
        name: p.name,
        chartColor: isDefect ? (p.color ?? p.row.chartColor) : undefined,
        isArchived: p.isArchived ?? p.row.isArchived,
      }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setEditing(null)
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (row: TypeRow) => api.delete<ApiMessage>(`${path}/${row.id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const rows = list.data ?? []

  return (
    <Card
      title={isDefect ? 'Defect Types' : 'Adder Types'}
      actions={<Checkbox checked={showArchived} onChange={setShowArchived} label="Show archived" />}
    >
      <form
        className="flex flex-wrap items-end gap-2 mb-3"
        onSubmit={(e) => {
          e.preventDefault()
          if (!name.trim()) return setFormError(`${isDefect ? 'Defect' : 'Adder'} Type Name is required.`)
          create.mutate()
        }}
      >
        <div className="flex-1 min-w-[180px]">
          <label className="label" htmlFor={`${kind}-name`}>
            {isDefect ? 'Defect Type Name' : 'Adder Type Name'} <span className="text-destructive">*</span>
          </label>
          <input id={`${kind}-name`} className="input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} placeholder={isDefect ? 'e.g. Orange Peel' : 'e.g. Extra Sanding'} />
        </div>
        {isDefect && (
          <div>
            <label className="label" htmlFor="defect-color">Chart Colour</label>
            <input id="defect-color" type="color" className="h-9 w-14 cursor-pointer rounded-md border border-input bg-card p-1" value={color} onChange={(e) => setColor(e.target.value)} />
          </div>
        )}
        <button className="btn-primary" type="submit" disabled={create.isPending}>
          {create.isPending ? <Spinner /> : <Plus className="h-4 w-4" />} Add
        </button>
      </form>
      <ErrorBanner message={formError} />

      {list.isLoading ? (
        <LoadingBlock />
      ) : list.isError ? (
        <ErrorBanner message={errorMessage(list.error)} />
      ) : rows.length === 0 ? (
        <EmptyState title={`No ${noun}s yet`} description={isDefect ? 'Defect types appear as chips on the process page and colour the Dashboard defect chart.' : 'Adders are extra work recorded against a running process.'} />
      ) : (
        <ul className="divide-y rounded-md border">
          {rows.map((t) => {
            const isEditing = editing?.id === t.id
            return (
              <li key={t.id} className="flex flex-wrap items-center gap-2 px-3 py-2">
                {isEditing ? (
                  <>
                    {isDefect && (
                      <input type="color" aria-label="Chart colour" className="h-8 w-10 cursor-pointer rounded border border-input bg-card p-0.5" value={editing.color} onChange={(e) => setEditing({ ...editing, color: e.target.value })} />
                    )}
                    <input
                      className="input h-8 flex-1 min-w-[140px]"
                      autoFocus
                      value={editing.name}
                      onChange={(e) => setEditing({ ...editing, name: e.target.value })}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') update.mutate({ row: t, name: editing.name, color: editing.color })
                        if (e.key === 'Escape') setEditing(null)
                      }}
                    />
                    <button className="btn-icon text-success" title="Save" disabled={update.isPending} onClick={() => update.mutate({ row: t, name: editing.name, color: editing.color })}>
                      {update.isPending ? <Spinner /> : <Check className="h-4 w-4" />}
                    </button>
                    <button className="btn-icon" title="Cancel" onClick={() => setEditing(null)}>
                      <X className="h-4 w-4" />
                    </button>
                  </>
                ) : (
                  <>
                    {isDefect && <ColorDot color={t.chartColor} className="h-4 w-4" />}
                    <span className={t.isArchived ? 'flex-1 text-muted-foreground line-through' : 'flex-1 font-medium'}>{t.name}</span>
                    {t.usageCount > 0 && <span className="badge bg-muted text-muted-foreground" title="Times recorded">{t.usageCount} used</span>}
                    {t.isArchived && <span className="badge bg-amber-100 text-amber-800">Archived</span>}
                    {t.isArchived ? (
                      <button className="btn-icon" title="Restore" onClick={() => update.mutate({ row: t, name: t.name, isArchived: false })}>
                        <ArchiveRestore className="h-4 w-4" />
                      </button>
                    ) : (
                      <>
                        <button className="btn-icon" title="Edit" onClick={() => setEditing({ id: t.id, name: t.name, color: t.chartColor || DEFAULT_COLORS[0] })}>
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button className="btn-icon hover:text-destructive" title={t.usageCount > 0 ? 'Archive' : 'Delete'} onClick={() => setDeleting(t)}>
                          {t.usageCount > 0 ? <Archive className="h-4 w-4" /> : <Trash2 className="h-4 w-4" />}
                        </button>
                      </>
                    )}
                  </>
                )}
              </li>
            )
          })}
        </ul>
      )}

      <ConfirmDialog
        open={!!deleting}
        title={deleting && deleting.usageCount > 0 ? 'Archive Confirmation' : 'Delete Confirmation'}
        confirmLabel={deleting && deleting.usageCount > 0 ? 'Archive' : 'Yes'}
        busy={remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
        message={
          deleting &&
          (deleting.usageCount > 0
            ? `The "${deleting.name}" ${noun} has been recorded ${deleting.usageCount} time(s), so it will be archived instead of deleted. Existing records keep it; it can no longer be picked.`
            : `Are you sure you want to delete the "${deleting.name}" ${noun}?`)
        }
      />
    </Card>
  )
}
