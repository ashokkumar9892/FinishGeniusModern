import { useMemo, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Eye, GripVertical, Play, X } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { SearchSelect } from '@/components/SearchSelect'
import { ConfirmDialog, ErrorBanner, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import {
  ExecutionStatus, ProgressBar, StatusBadge, useStartProcess,
  type ApiMessage, type ExecutionRow, type ScheduleRef, type StatusFilter,
} from './shared'

const STATUS_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'InProgress', label: 'In progress' },
  { value: 'Completed', label: 'Completed' },
  { value: 'Cancelled', label: 'Cancelled' },
  { value: 'all', label: 'All' },
]

type Pending = { kind: 'complete' | 'cancel'; row: ExecutionRow } | null

export function ProgressTab() {
  const { groupId } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const navigate = useNavigate()
  const [scheduleId, setScheduleId] = useState<number | null>(null)
  const [status, setStatus] = useState<StatusFilter>('InProgress')
  const [pending, setPending] = useState<Pending>(null)
  const [drag, setDrag] = useState<{ id: number; overId: number } | null>(null)

  const schedules = useQuery({
    queryKey: ['process-schedules', groupId],
    queryFn: () => api.get<ScheduleRef[]>('/process-schedules', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })

  const listKey = ['executions', groupId, status] as const
  const executions = useQuery({
    queryKey: listKey,
    queryFn: () => api.get<ExecutionRow[]>('/my-work/executions', { params: { groupId, status } }).then((r) => r.data),
    enabled: groupId > 0,
    refetchInterval: 60_000,
  })
  const rows = useMemo(() => executions.data ?? [], [executions.data])

  const start = useStartProcess()

  const scheduleOptions = useMemo(
    () =>
      (schedules.data ?? [])
        .filter((s) => !s.isArchived)
        .map((s) => ({
          value: s.id,
          label: `${s.groupName}: ${s.name} (#${s.number})`,
          sub: [s.customerName, s.departmentName, `${s.stepCount} step${s.stepCount === 1 ? '' : 's'}`].filter(Boolean).join(' · '),
        })),
    [schedules.data],
  )

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['executions'] })
    qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
    qc.invalidateQueries({ queryKey: ['dashboard-departments'] })
  }

  const finish = useMutation({
    mutationFn: (p: NonNullable<Pending>) => api.post<ApiMessage>(`/my-work/executions/${p.row.id}/${p.kind}`).then((r) => r.data),
    onSuccess: (res, p) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['execution', p.row.id] })
      invalidate()
      setPending(null)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const reorder = useMutation({
    mutationFn: (ids: number[]) => api.put<ApiMessage>('/my-work/executions/order', { ids }).then((r) => r.data),
    onError: (e) => {
      toast.error(errorMessage(e))
      qc.invalidateQueries({ queryKey: ['executions'] })
    },
    onSettled: () => qc.invalidateQueries({ queryKey: ['executions', groupId] }),
  })

  // ---- Drag & drop reorder (pointer events: works with mouse, pen and touch) ----------------
  const inProgress = useMemo(
    () => rows.filter((r) => r.status === ExecutionStatus.InProgress).sort((a, b) => a.ordering - b.ordering || a.id - b.id),
    [rows],
  )

  const move = (id: number, targetId: number) => {
    if (id === targetId) return
    const ids = inProgress.map((r) => r.id)
    const from = ids.indexOf(id)
    const to = ids.indexOf(targetId)
    if (from < 0 || to < 0) return
    ids.splice(from, 1)
    ids.splice(to, 0, id)
    // Optimistic: reuse the existing ordering slots in the new order (same rule as the API).
    const slots = inProgress.map((r) => r.ordering).sort((a, b) => a - b)
    const newOrdering = new Map(ids.map((rid, i) => [rid, slots[i]]))
    qc.setQueryData<ExecutionRow[]>(listKey, (old) => old?.map((r) => (newOrdering.has(r.id) ? { ...r, ordering: newOrdering.get(r.id)! } : r)))
    reorder.mutate(ids)
  }

  const rowIdAt = (x: number, y: number) => {
    const el = document.elementFromPoint(x, y)
    const marker = el?.closest('tr')?.querySelector<HTMLElement>('[data-exec-id]')
    const id = Number(marker?.dataset.execId)
    return inProgress.some((r) => r.id === id) ? id : null
  }

  const onHandleDown = (e: PointerEvent<HTMLButtonElement>, id: number) => {
    if (e.button !== 0) return
    e.preventDefault()
    e.currentTarget.setPointerCapture(e.pointerId)
    setDrag({ id, overId: id })
  }
  const onHandleMove = (e: PointerEvent<HTMLButtonElement>) => {
    if (!drag) return
    const over = rowIdAt(e.clientX, e.clientY)
    if (over != null && over !== drag.overId) setDrag({ ...drag, overId: over })
  }
  const onHandleUp = () => {
    if (drag) move(drag.id, drag.overId)
    setDrag(null)
  }
  const onHandleKey = (e: KeyboardEvent<HTMLButtonElement>, id: number) => {
    const i = inProgress.findIndex((r) => r.id === id)
    if (e.key === 'ArrowUp' && i > 0) {
      e.preventDefault()
      move(id, inProgress[i - 1].id)
    } else if (e.key === 'ArrowDown' && i >= 0 && i < inProgress.length - 1) {
      e.preventDefault()
      move(id, inProgress[i + 1].id)
    }
  }

  const columns: Column<ExecutionRow>[] = [
    {
      key: 'ordering',
      header: 'Order',
      sortValue: (r) => r.ordering,
      className: 'w-20',
      cell: (r) => (
        <span className="inline-flex items-center gap-1" data-exec-id={r.id}>
          {r.status === ExecutionStatus.InProgress ? (
            <button
              type="button"
              className={clsx('btn-icon h-8 w-7 touch-none', drag?.id === r.id ? 'cursor-grabbing' : 'cursor-grab')}
              title="Drag to reorder (or focus and use the arrow keys)"
              aria-label={`Reorder process #${r.id}`}
              onPointerDown={(e) => onHandleDown(e, r.id)}
              onPointerMove={onHandleMove}
              onPointerUp={onHandleUp}
              onPointerCancel={() => setDrag(null)}
              onKeyDown={(e) => onHandleKey(e, r.id)}
            >
              <GripVertical className="h-4 w-4" />
            </button>
          ) : (
            <span className="w-7" />
          )}
          <span className="tabular-nums text-muted-foreground">{r.ordering}</span>
        </span>
      ),
    },
    { key: 'id', header: '#', sortValue: (r) => r.id, className: 'w-16' },
    { key: 'groupName', header: 'Group', hideBelow: 'lg' },
    { key: 'userName', header: 'Username', hideBelow: 'sm' },
    { key: 'startedAt', header: 'Date/Time', cell: (r) => <span className="whitespace-nowrap">{dateTime(r.startedAt)}</span>, hideBelow: 'sm' },
    {
      key: 'scheduleName',
      header: 'Process Schedule Name',
      sortValue: (r) => r.scheduleName,
      cell: (r) => (
        <div className="min-w-0">
          <div className="font-medium">{r.scheduleName}</div>
          <div className="text-xs text-muted-foreground">
            #{r.scheduleNumber}
            {r.status !== ExecutionStatus.InProgress && (
              <span className="ml-2">
                <StatusBadge status={r.status} />
              </span>
            )}
          </div>
        </div>
      ),
    } as Column<ExecutionRow>,
    {
      key: 'progress',
      header: 'Progress',
      hideBelow: 'md',
      sortValue: (r) => r.progress,
      className: 'w-48',
      cell: (r) => <ProgressBar done={r.checkedLines} total={r.totalLines} />,
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) =>
        r.status === ExecutionStatus.InProgress ? (
          <div className="flex items-center justify-end gap-1">
            <button className="btn-success btn-sm" onClick={() => navigate(`/my-work/${r.id}`)} title="Resume process">
              <Play className="h-3.5 w-3.5 fill-current" /> Resume
            </button>
            <button className="btn-icon text-success hover:text-success" title="Complete process" onClick={() => setPending({ kind: 'complete', row: r })}>
              <CheckCircle2 className="h-4 w-4" />
            </button>
            <button className="btn-icon text-destructive hover:text-destructive" title="Cancel process" onClick={() => setPending({ kind: 'cancel', row: r })}>
              <X className="h-4 w-4" />
            </button>
          </div>
        ) : (
          <button className="btn-secondary btn-sm" onClick={() => navigate(`/my-work/${r.id}`)} title="View process">
            <Eye className="h-3.5 w-3.5" /> View
          </button>
        ),
    },
  ]

  return (
    <div className="space-y-4">
      <div className="card p-3 sm:p-4">
        <div className="flex flex-col sm:flex-row gap-2 sm:items-center">
          <SearchSelect
            className="flex-1"
            options={scheduleOptions}
            value={scheduleId}
            onChange={setScheduleId}
            placeholder={schedules.isLoading ? 'Loading process schedules…' : 'Select a process schedule to start…'}
            emptyText="No process schedules in this group"
          />
          <button className="btn-primary h-10 px-4" disabled={!scheduleId || start.isPending} onClick={() => scheduleId && start.mutate(scheduleId)}>
            {start.isPending ? <Spinner /> : <Play className="h-4 w-4 fill-current" />} Start Process
          </button>
        </div>
        {schedules.isError && <div className="mt-2"><ErrorBanner message={errorMessage(schedules.error)} /></div>}
      </div>

      {executions.isError && <ErrorBanner message={errorMessage(executions.error)} />}

      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={executions.isLoading}
        initialSort={{ key: 'ordering', dir: 'asc' }}
        searchPlaceholder="Search processes…"
        searchText={(r) => `${r.id} ${r.groupName} ${r.userName} ${r.scheduleName} ${r.scheduleNumber} ${dateTime(r.startedAt)}`}
        rowClassName={(r) =>
          clsx(
            drag && drag.id === r.id && 'opacity-50',
            drag && drag.overId === r.id && drag.id !== r.id && 'bg-accent outline outline-2 -outline-offset-2 outline-primary/60',
          )
        }
        emptyTitle={status === 'InProgress' ? 'No processes in progress' : 'No processes found'}
        emptyDescription={status === 'InProgress' ? 'Pick a process schedule above and press "Start Process".' : undefined}
        toolbar={
          <div className="flex items-center gap-2">
            <label className="text-xs font-medium text-muted-foreground" htmlFor="mywork-status">
              Status
            </label>
            <select id="mywork-status" className="input h-9 w-40" value={status} onChange={(e) => setStatus(e.target.value as StatusFilter)}>
              {STATUS_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
            {reorder.isPending && <Spinner className="text-muted-foreground" />}
          </div>
        }
      />

      <ConfirmDialog
        open={!!pending}
        title={pending?.kind === 'complete' ? 'Complete Process' : 'Cancel Process'}
        danger={pending?.kind === 'cancel'}
        confirmLabel={pending?.kind === 'complete' ? 'Complete process' : 'Cancel process'}
        busy={finish.isPending}
        onClose={() => setPending(null)}
        onConfirm={() => pending && finish.mutate(pending)}
        message={
          pending && (
            <>
              Are you sure you want to {pending.kind} process #{pending.row.id} "{pending.row.scheduleName}"?
              {pending.kind === 'complete' && pending.row.checkedLines < pending.row.totalLines && (
                <span className="block mt-2 text-amber-700 dark:text-amber-400">
                  Only {pending.row.checkedLines} of {pending.row.totalLines} checklist lines are checked.
                </span>
              )}
            </>
          )
        }
      />
    </div>
  )
}
