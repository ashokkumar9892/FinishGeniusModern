import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle, Ban, CheckCircle2, CircleCheck, CircleMinus, History, Minus, PlayCircle, Plus, PlusCircle, Trash2, Undo2,
} from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { dateTimeSeconds } from '@/lib/format'
import { SearchSelect } from '@/components/SearchSelect'
import { ConfirmDialog, EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import {
  ColorDot, type AdderRow, type AdderType, type ApiMessage, type DefectRow, type DefectType, type HistoryEvent, type HistoryKind,
} from './shared'

function useExecutionInvalidate(executionId: number) {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: ['execution', executionId] })
    qc.invalidateQueries({ queryKey: ['execution-history', executionId] })
    qc.invalidateQueries({ queryKey: ['executions'] })
    qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
  }
}

// ---------------------------------------------------------------------------
// Adders
// ---------------------------------------------------------------------------

export function AddersModal({ open, onClose, executionId, groupId, readOnly }: {
  open: boolean
  onClose: () => void
  executionId: number
  groupId: number
  readOnly: boolean
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const invalidate = useExecutionInvalidate(executionId)
  const [typeId, setTypeId] = useState<number | null>(null)
  const [value, setValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<AdderRow | null>(null)

  const list = useQuery({
    queryKey: ['execution-adders', executionId],
    queryFn: () => api.get<AdderRow[]>(`/my-work/executions/${executionId}/adders`).then((r) => r.data),
    enabled: open,
  })
  const types = useQuery({
    queryKey: ['adder-types', groupId, false],
    queryFn: () => api.get<AdderType[]>('/my-work/adder-types', { params: { groupId } }).then((r) => r.data),
    enabled: open && !readOnly,
  })

  const add = useMutation({
    mutationFn: () => api.post<ApiMessage>(`/my-work/executions/${executionId}/adders`, { adderTypeId: typeId, value }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setValue('')
      setError(null)
      qc.invalidateQueries({ queryKey: ['execution-adders', executionId] })
      qc.invalidateQueries({ queryKey: ['adder-types'] })
      invalidate()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (row: AdderRow) => api.delete<ApiMessage>(`/my-work/adders/${row.id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['execution-adders', executionId] })
      invalidate()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const submit = () => {
    if (!typeId) return setError('Adder type is required.')
    if (!value.trim()) return setError('Value is required.')
    add.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} title="Adders" size="lg" footer={<button className="btn-secondary h-11 px-5" onClick={onClose}>Close</button>}>
      {!readOnly && (
        <div className="rounded-lg border bg-muted/30 p-3 mb-4">
          <ErrorBanner message={error} />
          <div className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
            <Field label="Adder Type" required>
              <SearchSelect
                className="[&>button]:h-11"
                options={(types.data ?? []).map((t) => ({ value: t.id, label: t.name }))}
                value={typeId}
                onChange={setTypeId}
                placeholder={types.isLoading ? 'Loading…' : 'Select adder type…'}
                emptyText="No adder types — add them in My Work › Processes Management"
              />
            </Field>
            <Field label="Value" required>
              <input className="input h-11" value={value} maxLength={400} onChange={(e) => setValue(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} placeholder="e.g. 15 min, 2 parts" />
            </Field>
            <button className="btn-primary h-11 px-5" onClick={submit} disabled={add.isPending}>
              {add.isPending ? <Spinner /> : <Plus className="h-4 w-4" />} Add Adder
            </button>
          </div>
        </div>
      )}

      {list.isLoading ? (
        <LoadingBlock />
      ) : list.isError ? (
        <ErrorBanner message={errorMessage(list.error)} />
      ) : !list.data?.length ? (
        <EmptyState title="No adders recorded" description={readOnly ? undefined : 'Record extra work (e.g. extra sanding or touch up) against this process.'} />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/60 border-b">
              <tr>
                <th className="th">Adder</th>
                <th className="th">Value</th>
                <th className="th hidden sm:table-cell">Recorded</th>
                {!readOnly && <th className="th w-12" />}
              </tr>
            </thead>
            <tbody>
              {list.data.map((a) => (
                <tr key={a.id} className="border-b last:border-0">
                  <td className="td font-medium">{a.adderTypeName}</td>
                  <td className="td">{a.value}</td>
                  <td className="td hidden sm:table-cell text-xs text-muted-foreground whitespace-nowrap">
                    {dateTimeSeconds(a.createdAt)} · {a.userName}
                  </td>
                  {!readOnly && (
                    <td className="td text-right">
                      <button className="btn-icon hover:text-destructive" title="Remove adder" onClick={() => setDeleting(a)}>
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ConfirmDialog
        open={!!deleting}
        busy={remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
        message={deleting && `Are you sure you want to delete the "${deleting.adderTypeName}: ${deleting.value}" adder?`}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Defects
// ---------------------------------------------------------------------------

export function DefectsModal({ open, onClose, executionId, groupId, readOnly }: {
  open: boolean
  onClose: () => void
  executionId: number
  groupId: number
  readOnly: boolean
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const invalidate = useExecutionInvalidate(executionId)
  const [typeId, setTypeId] = useState<number | null>(null)
  const [quantity, setQuantity] = useState(1)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<DefectRow | null>(null)

  const list = useQuery({
    queryKey: ['execution-defects', executionId],
    queryFn: () => api.get<DefectRow[]>(`/my-work/executions/${executionId}/defects`).then((r) => r.data),
    enabled: open,
  })
  const types = useQuery({
    queryKey: ['defect-types', groupId, false],
    queryFn: () => api.get<DefectType[]>('/my-work/defect-types', { params: { groupId } }).then((r) => r.data),
    enabled: open && !readOnly,
  })

  const add = useMutation({
    mutationFn: () => api.post<ApiMessage>(`/my-work/executions/${executionId}/defects`, { defectTypeId: typeId, quantity, notes }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setQuantity(1)
      setNotes('')
      setError(null)
      qc.invalidateQueries({ queryKey: ['execution-defects', executionId] })
      qc.invalidateQueries({ queryKey: ['defect-types'] })
      invalidate()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (row: DefectRow) => api.delete<ApiMessage>(`/my-work/defects/${row.id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['execution-defects', executionId] })
      invalidate()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const submit = () => {
    if (!typeId) return setError('Pick a defect type.')
    if (!Number.isInteger(quantity) || quantity < 1) return setError('Quantity must be at least 1.')
    add.mutate()
  }

  const total = (list.data ?? []).reduce((s, d) => s + d.quantity, 0)

  return (
    <Modal open={open} onClose={onClose} title="Defects" size="lg" footer={<button className="btn-secondary h-11 px-5" onClick={onClose}>Close</button>}>
      {!readOnly && (
        <div className="rounded-lg border bg-muted/30 p-3 mb-4 space-y-3">
          <ErrorBanner message={error} />
          <div>
            <div className="label">Defect Type <span className="text-destructive">*</span></div>
            {types.isLoading ? (
              <LoadingBlock />
            ) : !types.data?.length ? (
              <div className="text-sm text-muted-foreground">No defect types yet — add them in My Work › Processes Management.</div>
            ) : (
              <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Defect type">
                {types.data.map((t) => {
                  const active = typeId === t.id
                  return (
                    <button
                      key={t.id}
                      type="button"
                      role="radio"
                      aria-checked={active}
                      onClick={() => setTypeId(t.id)}
                      className={clsx(
                        'inline-flex items-center gap-2 rounded-full border-2 px-4 h-11 text-sm font-medium transition-colors',
                        active ? 'bg-card shadow-sm' : 'border-border bg-card hover:bg-muted',
                      )}
                      style={active ? { borderColor: t.chartColor || 'hsl(var(--primary))' } : undefined}
                    >
                      <ColorDot color={t.chartColor} className="h-3.5 w-3.5" />
                      {t.name}
                      {active && <CheckCircle2 className="h-4 w-4" style={{ color: t.chartColor || undefined }} />}
                    </button>
                  )
                })}
              </div>
            )}
          </div>
          <div className="grid gap-3 sm:grid-cols-[auto_1fr_auto] sm:items-end">
            <Field label="Quantity" required>
              <div className="flex items-center gap-1">
                <button type="button" className="btn-secondary h-11 w-11 px-0" aria-label="Decrease quantity" onClick={() => setQuantity((q) => Math.max(1, q - 1))}>
                  <Minus className="h-4 w-4" />
                </button>
                <input
                  className="input h-11 w-20 text-center tabular-nums"
                  inputMode="numeric"
                  value={quantity}
                  onChange={(e) => setQuantity(Math.max(0, parseInt(e.target.value.replace(/\D/g, '') || '0', 10)))}
                  aria-label="Quantity"
                />
                <button type="button" className="btn-secondary h-11 w-11 px-0" aria-label="Increase quantity" onClick={() => setQuantity((q) => q + 1)}>
                  <Plus className="h-4 w-4" />
                </button>
              </div>
            </Field>
            <Field label="Notes">
              <input className="input h-11" value={notes} maxLength={400} onChange={(e) => setNotes(e.target.value)} placeholder="Optional — where / which part" onKeyDown={(e) => e.key === 'Enter' && submit()} />
            </Field>
            <button className="btn-primary h-11 px-5" onClick={submit} disabled={add.isPending}>
              {add.isPending ? <Spinner /> : <Plus className="h-4 w-4" />} Add Defect
            </button>
          </div>
        </div>
      )}

      {list.isLoading ? (
        <LoadingBlock />
      ) : list.isError ? (
        <ErrorBanner message={errorMessage(list.error)} />
      ) : !list.data?.length ? (
        <EmptyState title="No defects recorded" icon={<CircleCheck className="h-5 w-5" />} />
      ) : (
        <>
          <div className="mb-2 text-xs text-muted-foreground">
            {list.data.length} record{list.data.length === 1 ? '' : 's'} · {total} defect{total === 1 ? '' : 's'} in total
          </div>
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/60 border-b">
                <tr>
                  <th className="th">Defect</th>
                  <th className="th text-right">Qty</th>
                  <th className="th hidden sm:table-cell">Notes</th>
                  <th className="th hidden md:table-cell">Recorded</th>
                  {!readOnly && <th className="th w-12" />}
                </tr>
              </thead>
              <tbody>
                {list.data.map((d) => (
                  <tr key={d.id} className="border-b last:border-0">
                    <td className="td">
                      <span className="inline-flex items-center gap-2 font-medium">
                        <ColorDot color={d.chartColor} /> {d.defectTypeName}
                      </span>
                    </td>
                    <td className="td text-right tabular-nums font-semibold">{d.quantity}</td>
                    <td className="td hidden sm:table-cell text-muted-foreground">{d.notes}</td>
                    <td className="td hidden md:table-cell text-xs text-muted-foreground whitespace-nowrap">
                      {dateTimeSeconds(d.createdAt)} · {d.userName}
                    </td>
                    {!readOnly && (
                      <td className="td text-right">
                        <button className="btn-icon hover:text-destructive" title="Remove defect" onClick={() => setDeleting(d)}>
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <ConfirmDialog
        open={!!deleting}
        busy={remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
        message={deleting && `Are you sure you want to delete the "${deleting.defectTypeName}" defect (quantity ${deleting.quantity})?`}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// History timeline
// ---------------------------------------------------------------------------

const KIND: Record<HistoryKind, { icon: typeof History; className: string }> = {
  started: { icon: PlayCircle, className: 'bg-primary text-primary-foreground' },
  checked: { icon: CheckCircle2, className: 'bg-success text-success-foreground' },
  unchecked: { icon: Undo2, className: 'bg-slate-500 text-white' },
  defect: { icon: AlertTriangle, className: 'bg-destructive text-destructive-foreground' },
  defectRemoved: { icon: CircleMinus, className: 'bg-slate-500 text-white' },
  adder: { icon: PlusCircle, className: 'bg-sky-600 text-white' },
  adderRemoved: { icon: CircleMinus, className: 'bg-slate-500 text-white' },
  completed: { icon: CircleCheck, className: 'bg-success text-success-foreground' },
  cancelled: { icon: Ban, className: 'bg-destructive text-destructive-foreground' },
  other: { icon: History, className: 'bg-muted text-muted-foreground' },
}

const FILTERS: { key: string; label: string; kinds: HistoryKind[] | null }[] = [
  { key: 'all', label: 'All', kinds: null },
  { key: 'checks', label: 'Checks', kinds: ['checked', 'unchecked'] },
  { key: 'defects', label: 'Defects', kinds: ['defect', 'defectRemoved'] },
  { key: 'adders', label: 'Adders', kinds: ['adder', 'adderRemoved'] },
  { key: 'status', label: 'Status', kinds: ['started', 'completed', 'cancelled'] },
]

export function ExecutionHistoryModal({ open, onClose, executionId }: { open: boolean; onClose: () => void; executionId: number }) {
  const [filter, setFilter] = useState('all')
  const q = useQuery({
    queryKey: ['execution-history', executionId],
    queryFn: () => api.get<HistoryEvent[]>(`/my-work/executions/${executionId}/history`).then((r) => r.data),
    enabled: open,
  })
  const kinds = FILTERS.find((f) => f.key === filter)?.kinds
  const events = (q.data ?? []).filter((e) => !kinds || kinds.includes(e.kind))

  return (
    <Modal open={open} onClose={onClose} title="Process History" size="lg" footer={<button className="btn-secondary h-11 px-5" onClick={onClose}>Close</button>}>
      <div className="flex flex-wrap gap-1.5 mb-4">
        {FILTERS.map((f) => (
          <button key={f.key} type="button" className={clsx('btn-sm btn rounded-full border', filter === f.key ? 'bg-primary text-primary-foreground border-primary' : 'bg-card hover:bg-muted')} onClick={() => setFilter(f.key)}>
            {f.label}
          </button>
        ))}
      </div>
      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : events.length === 0 ? (
        <EmptyState title="No history recorded yet" icon={<History className="h-5 w-5" />} />
      ) : (
        <ol className="relative ml-4 border-l space-y-4">
          {events.map((e) => {
            const k = KIND[e.kind] ?? KIND.other
            const Icon = k.icon
            return (
              <li key={`${e.id}-${e.createdAt}`} className="ml-6">
                <span className={clsx('absolute -left-3.5 flex h-7 w-7 items-center justify-center rounded-full ring-4 ring-card', k.className)}>
                  <Icon className="h-4 w-4" />
                </span>
                <div className="text-sm font-medium">{e.action}</div>
                <div className="text-xs text-muted-foreground">
                  {dateTimeSeconds(e.createdAt)} · {e.userName || 'system'}
                </div>
                {e.details && <div className="mt-1 text-sm text-foreground/90 break-words">{e.details}</div>}
              </li>
            )
          })}
        </ol>
      )}
    </Modal>
  )
}
