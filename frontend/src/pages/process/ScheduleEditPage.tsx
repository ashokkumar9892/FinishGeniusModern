import { useEffect, useMemo, useRef, useState, type DragEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowLeft, ArrowUp, ChevronLeft, ChevronRight, Eye, GripVertical, Pencil, Printer, RotateCcw, Save } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { toNumber } from '@/lib/format'
import { useToast } from '@/components/toast'
import { SearchSelect } from '@/components/SearchSelect'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note, PageHeader, SearchInput, Spinner } from '@/components/ui'
import { confirmLeave, PassList, useUnsavedGuard, ViewStepModal } from './shared'
import { SchedulePrintModal } from './SchedulePrint'
import type { ProcessStepRow, ScheduleDetail, StepEditPayload } from './types'

interface AssignedRow {
  uid: number
  scheduleStepId?: number
  processStepId: number
  name: string
  originalName: string
  hasOverrides: boolean
  deleted?: boolean
}

const PAGE = 10

export default function ScheduleEditPage() {
  const { id } = useParams()
  const scheduleId = id && id !== 'new' && Number(id) > 0 ? Number(id) : null
  const navigate = useNavigate()
  const { groupId, group } = useGroup()
  const toast = useToast()
  const qc = useQueryClient()
  const uidRef = useRef(1)
  const nextUid = () => uidRef.current++

  const [name, setName] = useState('')
  const [number, setNumber] = useState('')
  const [customer, setCustomer] = useState('')
  const [assigned, setAssigned] = useState<AssignedRow[]>([])
  const [initialSig, setInitialSig] = useState('')
  const [errors, setErrors] = useState<{ name?: string; number?: string }>({})
  const [serverError, setServerError] = useState<string | null>(null)

  const [availSearch, setAvailSearch] = useState('')
  const [availSel, setAvailSel] = useState<Set<number>>(new Set())
  const [availPage, setAvailPage] = useState(0)
  const [assSearch, setAssSearch] = useState('')
  const [assSel, setAssSel] = useState<Set<number>>(new Set())
  const [dragUid, setDragUid] = useState<number | null>(null)
  const [dropUid, setDropUid] = useState<number | null>(null)

  const [viewing, setViewing] = useState<AssignedRow | null>(null)
  const [editing, setEditing] = useState<AssignedRow | null>(null)
  const [printOpen, setPrintOpen] = useState(false)

  const detail = useQuery({
    queryKey: ['process-schedule', scheduleId],
    queryFn: () => api.get<ScheduleDetail>(`/process-schedules/${scheduleId}`).then((r) => r.data),
    enabled: !!scheduleId,
  })
  const scheduleGroupId = detail.data?.groupId ?? groupId
  const steps = useQuery({
    queryKey: ['process-steps', scheduleGroupId, 0, ''],
    queryFn: () => api.get<ProcessStepRow[]>('/process-steps', { params: { groupId: scheduleGroupId } }).then((r) => r.data),
  })

  const signature = (n: string, num: string, c: string, rows: AssignedRow[]) =>
    JSON.stringify([n.trim(), num.trim(), c.trim(), rows.map((r) => [r.scheduleStepId ?? 0, r.processStepId])])

  // Initialise the form from the server (on load and after each save).
  useEffect(() => {
    if (!scheduleId) {
      setInitialSig(signature('', '', '', []))
      return
    }
    const d = detail.data
    if (!d) return
    const rows = d.steps.map((s) => ({
      uid: nextUid(),
      scheduleStepId: s.scheduleStepId,
      processStepId: s.processStepId,
      name: s.name,
      originalName: s.originalName,
      hasOverrides: s.hasOverrides,
      deleted: s.processStepDeleted,
    }))
    setName(d.name)
    setNumber(d.number)
    setCustomer(d.customerName ?? '')
    setAssigned(rows)
    setAssSel(new Set())
    setInitialSig(signature(d.name, d.number, d.customerName ?? '', rows))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detail.data, scheduleId])

  const dirty = signature(name, number, customer, assigned) !== initialSig
  useUnsavedGuard(dirty)
  const archived = !!detail.data?.isArchived

  // ---------- Available steps ----------
  const available = useMemo(() => {
    const s = availSearch.trim().toLowerCase()
    return (steps.data ?? []).filter((x) => !s || `${x.name} ${x.industrySectorName}`.toLowerCase().includes(s)).sort((a, b) => a.name.localeCompare(b.name))
  }, [steps.data, availSearch])
  const availPages = Math.max(1, Math.ceil(available.length / PAGE))
  const curPage = Math.min(availPage, availPages - 1)
  const availRows = available.slice(curPage * PAGE, curPage * PAGE + PAGE)
  useEffect(() => setAvailPage(0), [availSearch])
  const allAvailSelected = available.length > 0 && available.every((x) => availSel.has(x.id))
  const assignedCount = useMemo(() => {
    const m = new Map<number, number>()
    assigned.forEach((r) => m.set(r.processStepId, (m.get(r.processStepId) ?? 0) + 1))
    return m
  }, [assigned])

  const add = () => {
    const picked = available.filter((x) => availSel.has(x.id))
    setAssigned((a) => [...a, ...picked.map((x) => ({ uid: nextUid(), processStepId: x.id, name: x.name, originalName: x.name, hasOverrides: false }))])
    setAvailSel(new Set())
  }
  const remove = () => {
    setAssigned((a) => a.filter((r) => !assSel.has(r.uid)))
    setAssSel(new Set())
  }

  // ---------- Assigned steps ----------
  const assFilter = assSearch.trim().toLowerCase()
  const visibleAssigned = assigned.map((r, i) => ({ r, i })).filter(({ r }) => !assFilter || r.name.toLowerCase().includes(assFilter))
  const allAssSelected = visibleAssigned.length > 0 && visibleAssigned.every(({ r }) => assSel.has(r.uid))

  const move = (from: number, to: number) =>
    setAssigned((a) => {
      if (to < 0 || to >= a.length || from === to) return a
      const next = [...a]
      const [x] = next.splice(from, 1)
      next.splice(to, 0, x)
      return next
    })

  const onDrop = (e: DragEvent, targetUid: number) => {
    e.preventDefault()
    if (dragUid == null || dragUid === targetUid) return
    const from = assigned.findIndex((r) => r.uid === dragUid)
    const to = assigned.findIndex((r) => r.uid === targetUid)
    move(from, to)
    setDragUid(null)
    setDropUid(null)
  }

  // ---------- Save ----------
  const save = useMutation({
    mutationFn: () => {
      const body = {
        groupId: scheduleGroupId,
        name: name.trim(),
        number: number.trim(),
        customerName: customer.trim() || null,
        steps: assigned.map((r) => ({ scheduleStepId: r.scheduleStepId ?? null, processStepId: r.processStepId })),
      }
      return scheduleId ? api.put(`/process-schedules/${scheduleId}`, body) : api.post('/process-schedules', body)
    },
    onSuccess: async (res) => {
      toast.success(res.data.message)
      setServerError(null)
      qc.invalidateQueries({ queryKey: ['process-schedules'] })
      qc.invalidateQueries({ queryKey: ['process-steps'] })
      qc.invalidateQueries({ queryKey: ['process-schedule-print'] })
      if (!scheduleId) {
        setInitialSig(signature(name, number, customer, assigned)) // avoid the leave prompt
        navigate(`/process-schedules/${res.data.id}`, { replace: true })
      } else await qc.invalidateQueries({ queryKey: ['process-schedule', scheduleId] })
    },
    onError: (e) => {
      const msg = errorMessage(e)
      setServerError(msg)
      toast.error(msg)
    },
  })

  const submit = () => {
    const e: typeof errors = {}
    if (!name.trim()) e.name = 'Schedule Name is required.'
    if (!number.trim()) e.number = 'Schedule # is required.'
    setErrors(e)
    if (Object.keys(e).length === 0) save.mutate()
  }

  const title = scheduleId ? `Edit Process Schedule #${scheduleId}` : 'Create Process Schedule'

  if (scheduleId && detail.isError)
    return (
      <>
        <PageHeader title={title} breadcrumbs={[['Process Schedule List', '/process-schedules'], title]} />
        <ErrorBanner message={errorMessage(detail.error)} />
        <button className="btn-secondary" onClick={() => navigate('/process-schedules')}><ArrowLeft className="h-4 w-4" /> Back</button>
      </>
    )

  return (
    <>
      <PageHeader
        title={title}
        subtitle={detail.data ? `${detail.data.name} · ${detail.data.number}` : undefined}
        breadcrumbs={[['Process Schedule List', '/process-schedules'], title]}
        actions={
          <>
            <button className="btn-ghost" onClick={() => confirmLeave(dirty) && navigate('/process-schedules')}>
              <ArrowLeft className="h-4 w-4" /> Back
            </button>
            {scheduleId && (
              <button className="btn-secondary" onClick={() => setPrintOpen(true)} title={dirty ? 'Prints the saved schedule' : undefined}>
                <Printer className="h-4 w-4" /> Print
              </button>
            )}
            <button className="btn-primary" onClick={submit} disabled={save.isPending || archived || (!!scheduleId && !dirty)}>
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        }
      />

      {scheduleId && detail.isLoading ? (
        <LoadingBlock />
      ) : (
        <>
          {archived && (
            <div className="mb-4">
              <Note>This schedule is archived and read-only. An administrator can restore it from the Process Schedule List ("Show archived").</Note>
            </div>
          )}
          <ErrorBanner message={serverError} />
          <div className="card p-4 mb-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Group">
              <input className="input" value={detail.data?.groupName ?? group?.name ?? ''} disabled readOnly />
            </Field>
            <Field label="Schedule Name" required error={errors.name}>
              <input className={clsx('input', errors.name && 'input-invalid')} value={name} maxLength={400} disabled={archived} onChange={(e) => setName(e.target.value)} />
            </Field>
            <Field label="Schedule #" required error={errors.number}>
              <input className={clsx('input', errors.number && 'input-invalid')} value={number} maxLength={400} disabled={archived} onChange={(e) => setNumber(e.target.value)} />
            </Field>
            <Field label="Customer Name">
              <input className="input" value={customer} maxLength={400} disabled={archived} onChange={(e) => setCustomer(e.target.value)} />
            </Field>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-start">
            {/* Available */}
            <section className="card flex flex-col min-h-[420px]">
              <div className="flex items-center justify-between gap-2 border-b px-4 py-3">
                <h2 className="font-semibold">Available Steps</h2>
                <span className="text-xs text-muted-foreground">{available.length} steps</span>
              </div>
              <div className="p-3 border-b flex items-center gap-3">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-[hsl(var(--primary))]"
                  checked={allAvailSelected}
                  disabled={archived || available.length === 0}
                  onChange={() => setAvailSel(allAvailSelected ? new Set() : new Set(available.map((x) => x.id)))}
                  aria-label="Select all available steps"
                />
                <SearchInput value={availSearch} onChange={setAvailSearch} placeholder="Search steps…" className="flex-1" />
              </div>
              <ul className="flex-1 divide-y">
                {steps.isLoading && <LoadingBlock />}
                {!steps.isLoading && availRows.length === 0 && (
                  <EmptyState title={availSearch ? 'No matching steps' : 'No process steps in this group'} description={availSearch ? undefined : 'Create process steps first.'} />
                )}
                {availRows.map((x) => (
                  <li key={x.id}>
                    <label className={clsx('flex items-center gap-3 px-4 py-2 text-sm', archived ? 'opacity-60' : 'cursor-pointer hover:bg-muted/40', availSel.has(x.id) && 'bg-accent')}>
                      <input
                        type="checkbox"
                        className="h-4 w-4 accent-[hsl(var(--primary))]"
                        checked={availSel.has(x.id)}
                        disabled={archived}
                        onChange={() =>
                          setAvailSel((s) => {
                            const n = new Set(s)
                            if (n.has(x.id)) n.delete(x.id)
                            else n.add(x.id)
                            return n
                          })
                        }
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{x.name}</span>
                        <span className="block text-xs text-muted-foreground">
                          {x.industrySectorName} · {x.filledCount} sub steps filled
                        </span>
                      </span>
                      {assignedCount.get(x.id) ? <span className="badge bg-muted text-muted-foreground" title="Times assigned">×{assignedCount.get(x.id)}</span> : null}
                    </label>
                  </li>
                ))}
              </ul>
              {availPages > 1 && (
                <div className="flex items-center justify-between border-t px-3 py-2 text-xs text-muted-foreground">
                  <span>Page {curPage + 1} of {availPages}</span>
                  <div className="flex gap-1">
                    <button className="btn-ghost btn-sm" disabled={curPage === 0} onClick={() => setAvailPage(curPage - 1)}><ChevronLeft className="h-3.5 w-3.5" /> Prev</button>
                    <button className="btn-ghost btn-sm" disabled={curPage >= availPages - 1} onClick={() => setAvailPage(curPage + 1)}>Next <ChevronRight className="h-3.5 w-3.5" /></button>
                  </div>
                </div>
              )}
            </section>

            {/* Transfer buttons */}
            <div className="flex lg:flex-col items-center justify-center gap-2 lg:pt-40">
              <button className="btn-primary w-28" disabled={archived || availSel.size === 0} onClick={add}>
                add <ChevronRight className="h-4 w-4" />
              </button>
              <button className="btn-secondary w-28" disabled={archived || assSel.size === 0} onClick={remove}>
                <ChevronLeft className="h-4 w-4" /> remove
              </button>
            </div>

            {/* Assigned */}
            <section className="card flex flex-col min-h-[420px]">
              <div className="flex items-center justify-between gap-2 border-b px-4 py-3">
                <h2 className="font-semibold">Assigned Steps</h2>
                <span className="text-xs text-muted-foreground">{assigned.length} steps</span>
              </div>
              <div className="p-3 border-b flex items-center gap-3">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-[hsl(var(--primary))]"
                  checked={allAssSelected}
                  disabled={archived || visibleAssigned.length === 0}
                  onChange={() => setAssSel(allAssSelected ? new Set() : new Set(visibleAssigned.map(({ r }) => r.uid)))}
                  aria-label="Select all assigned steps"
                />
                <SearchInput value={assSearch} onChange={setAssSearch} placeholder="Search assigned…" className="flex-1" />
              </div>
              {assigned.length === 0 ? (
                <EmptyState title="No steps assigned" description='Tick steps on the left and press "add ›". The same step may be added more than once.' />
              ) : (
                <ol className="flex-1 divide-y">
                  {visibleAssigned.map(({ r, i }) => (
                    <li
                      key={r.uid}
                      draggable={!archived && !assFilter}
                      onDragStart={(e) => {
                        setDragUid(r.uid)
                        e.dataTransfer.effectAllowed = 'move'
                        e.dataTransfer.setData('text/plain', String(r.uid))
                      }}
                      onDragOver={(e) => {
                        if (dragUid == null) return
                        e.preventDefault()
                        setDropUid(r.uid)
                      }}
                      onDragLeave={() => setDropUid((d) => (d === r.uid ? null : d))}
                      onDrop={(e) => onDrop(e, r.uid)}
                      onDragEnd={() => {
                        setDragUid(null)
                        setDropUid(null)
                      }}
                      className={clsx(
                        'flex items-center gap-2 px-3 py-2 text-sm transition-colors',
                        assSel.has(r.uid) && 'bg-accent',
                        dragUid === r.uid && 'opacity-40',
                        dropUid === r.uid && dragUid !== r.uid && 'ring-2 ring-inset ring-primary',
                      )}
                    >
                      <GripVertical className={clsx('h-4 w-4 shrink-0 text-muted-foreground', !archived && !assFilter ? 'cursor-grab' : 'opacity-30')} aria-hidden />
                      <input
                        type="checkbox"
                        className="h-4 w-4 accent-[hsl(var(--primary))]"
                        checked={assSel.has(r.uid)}
                        disabled={archived}
                        onChange={() =>
                          setAssSel((s) => {
                            const n = new Set(s)
                            if (n.has(r.uid)) n.delete(r.uid)
                            else n.add(r.uid)
                            return n
                          })
                        }
                        aria-label={`Select ${r.name}`}
                      />
                      <span className="w-6 text-right text-xs font-semibold text-muted-foreground tabular-nums">{i + 1}</span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{r.name}</span>
                        <span className="flex flex-wrap gap-1.5 text-xs text-muted-foreground">
                          {r.name !== r.originalName && <span className="truncate">step: {r.originalName}</span>}
                          {r.hasOverrides && <span className="badge bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300">edited</span>}
                          {!r.scheduleStepId && <span className="badge bg-amber-100 text-amber-800">new</span>}
                          {r.deleted && <span className="badge bg-red-100 text-red-700">step deleted</span>}
                        </span>
                      </span>
                      <div className="flex shrink-0">
                        <button className="btn-icon h-7 w-7" title="Move up" disabled={archived || i === 0} onClick={() => move(i, i - 1)}><ArrowUp className="h-3.5 w-3.5" /></button>
                        <button className="btn-icon h-7 w-7" title="Move down" disabled={archived || i === assigned.length - 1} onClick={() => move(i, i + 1)}><ArrowDown className="h-3.5 w-3.5" /></button>
                        <button className="btn-icon h-7 w-7" title="View Step" onClick={() => setViewing(r)}><Eye className="h-3.5 w-3.5" /></button>
                        <button
                          className="btn-icon h-7 w-7"
                          title={r.scheduleStepId ? 'Edit Step' : 'Save the schedule before editing this step'}
                          disabled={archived || !r.scheduleStepId}
                          onClick={() => setEditing(r)}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    </li>
                  ))}
                  {visibleAssigned.length === 0 && <li className="px-4 py-6 text-center text-sm text-muted-foreground">No matching assigned steps</li>}
                </ol>
              )}
              {assigned.length > 1 && (
                <div className="border-t px-3 py-2 text-xs text-muted-foreground">
                  {assFilter ? 'Clear the search to reorder.' : 'Drag ☰ or use the arrows to reorder.'}
                </div>
              )}
            </section>
          </div>
        </>
      )}

      <ViewStepModal open={!!viewing} onClose={() => setViewing(null)} stepId={viewing?.processStepId ?? null} title={viewing?.originalName} />
      <EditStepModal
        row={editing}
        onClose={() => setEditing(null)}
        onSaved={(scheduleStepId, newName, hasOverrides) => {
          const patch = (rows: AssignedRow[]) => rows.map((x) => (x.scheduleStepId === scheduleStepId ? { ...x, name: newName, hasOverrides } : x))
          setAssigned(patch)
          if (scheduleId) qc.invalidateQueries({ queryKey: ['process-schedule', scheduleId], refetchType: 'none' })
        }}
      />
      {scheduleId && <SchedulePrintModal open={printOpen} scheduleId={scheduleId} onClose={() => setPrintOpen(false)} />}
    </>
  )
}

// ---------------------------------------------------------------------------
// Edit Step (schedule-level name + value overrides / input ranges)
// ---------------------------------------------------------------------------

interface RowDraft {
  value: string
  min: string
  max: string
}

function EditStepModal({ row, onClose, onSaved }: {
  row: AssignedRow | null
  onClose: () => void
  onSaved: (scheduleStepId: number, name: string, hasOverrides: boolean) => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const { groupId } = useGroup()
  const open = !!row?.scheduleStepId
  const q = useQuery({
    queryKey: ['schedule-step-edit', row?.scheduleStepId],
    queryFn: () => api.get<StepEditPayload>(`/process-schedules/steps/${row!.scheduleStepId}/edit`).then((r) => r.data),
    enabled: open,
    staleTime: 0,
  })
  const materials = useQuery({
    queryKey: ['materials', q.data?.groupId ?? groupId, 'all'],
    queryFn: () =>
      api.get<{ id: number; productName: string; productCode?: string | null; categoryName?: string | null }[]>('/materials', { params: { groupId: q.data?.groupId ?? groupId } }).then((r) => r.data),
    enabled: open && !!q.data?.values.some((v) => v.inputType === 'Material'),
    staleTime: 60_000,
  })
  const materialOptions = useMemo(
    () => (materials.data ?? []).map((m) => ({ value: m.id, label: m.productCode ? `${m.productName} (${m.productCode})` : m.productName, sub: m.categoryName ?? undefined })),
    [materials.data],
  )

  const [stepName, setStepName] = useState('')
  const [drafts, setDrafts] = useState<Record<number, RowDraft>>({})
  const [error, setError] = useState<string | null>(null)
  const [showPasses, setShowPasses] = useState(false)

  useEffect(() => {
    if (!q.data) return
    setStepName(q.data.name)
    const d: Record<number, RowDraft> = {}
    q.data.values.forEach((v) => {
      d[v.valueId] = {
        value: v.overrideValue ?? '',
        min: v.minValue != null ? String(v.minValue) : '',
        max: v.maxValue != null ? String(v.maxValue) : '',
      }
    })
    setDrafts(d)
    setError(null)
  }, [q.data])

  const save = useMutation({
    mutationFn: () =>
      api.put(`/process-schedules/steps/${row!.scheduleStepId}/edit`, {
        nameOverride: stepName.trim() || null,
        values: q.data!.values.map((v) => {
          const d = drafts[v.valueId] ?? { value: '', min: '', max: '' }
          return {
            valueId: v.valueId,
            value: d.value.trim() || null,
            minValue: d.min.trim() ? toNumber(d.min) : null,
            maxValue: d.max.trim() ? toNumber(d.max) : null,
          }
        }),
      }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      const effectiveName = stepName.trim() || q.data!.originalName
      const hasOverrides =
        effectiveName !== q.data!.originalName ||
        q.data!.values.some((v) => {
          const d = drafts[v.valueId]
          const same = !d?.value.trim() || (v.inputType === 'Material' ? d.value === String(v.originalMaterialId ?? '') : d.value.trim() === (v.originalValue ?? ''))
          return !same || !!d?.min.trim() || !!d?.max.trim()
        })
      onSaved(row!.scheduleStepId!, effectiveName, hasOverrides)
      qc.invalidateQueries({ queryKey: ['schedule-step-edit'] })
      qc.invalidateQueries({ queryKey: ['process-schedule-print'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    const bad = q.data?.values.find((v) => {
      const d = drafts[v.valueId]
      if (!d) return false
      const numeric = (s: string) => !s.trim() || /^-?\d*\.?\d+$/.test(s.trim().replace(/,/g, ''))
      if (!numeric(d.min) || !numeric(d.max)) return true
      if (v.inputType === 'Number' && !numeric(d.value)) return true
      return d.min.trim() && d.max.trim() && toNumber(d.min) > toNumber(d.max)
    })
    if (bad) return setError(`Check the value and range of "${bad.characteristic}" (numbers only, Min ≤ Max).`)
    setError(null)
    save.mutate()
  }

  const setD = (valueId: number, patch: Partial<RowDraft>) => setDrafts((d) => ({ ...d, [valueId]: { ...(d[valueId] ?? { value: '', min: '', max: '' }), ...patch } }))

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="xl"
      title={`Edit Step (${row?.name ?? ''})`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending || !q.data}>
            {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
          </button>
        </>
      }
    >
      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : q.data ? (
        <div className="space-y-5">
          <ErrorBanner message={error} />
          <Field label="Name" hint={`Renames the step in this schedule only. Process step: ${q.data.originalName}`}>
            <input className="input" value={stepName} maxLength={400} onChange={(e) => setStepName(e.target.value)} placeholder={q.data.originalName} />
          </Field>

          <div>
            <h4 className="font-semibold mb-1">Edit Input Range</h4>
            <p className="text-xs text-muted-foreground mb-2">
              Leave a value empty to keep the step's value. Min / Max set the allowed range recorded in My Work. These edits are kept by "Copy Master" and dropped by "Clone".
            </p>
            {q.data.values.length === 0 ? (
              <div className="rounded-md border px-4 py-6 text-center text-sm text-muted-foreground">This step has no saved values to edit.</div>
            ) : (
              <div className="overflow-x-auto rounded-md border">
                <table className="w-full text-sm">
                  <thead className="bg-muted/60 border-b">
                    <tr>
                      <th className="th w-14">Sub</th>
                      <th className="th">Characteristic</th>
                      <th className="th">Step value</th>
                      <th className="th min-w-[200px]">Schedule value</th>
                      <th className="th w-24">Min</th>
                      <th className="th w-24">Max</th>
                      <th className="th w-8" />
                    </tr>
                  </thead>
                  <tbody>
                    {q.data.values.map((v, i) => {
                      const d = drafts[v.valueId] ?? { value: '', min: '', max: '' }
                      const first = i === 0 || q.data!.values[i - 1].subStepLabel !== v.subStepLabel
                      const changed = !!d.value.trim() || !!d.min.trim() || !!d.max.trim()
                      const rangeable = v.inputType === 'Number'
                      return (
                        <tr key={v.valueId} className={clsx('border-b last:border-0', changed && 'bg-sky-50/60 dark:bg-sky-950/30')}>
                          <td className="td align-top font-semibold" title={v.subStepName}>{first ? v.subStepLabel : ''}</td>
                          <td className="td align-top">
                            <div className="font-medium">{v.characteristic}</div>
                            <div className="text-xs text-muted-foreground">{v.categoryName}{v.unit ? ` · ${v.unit}` : ''}</div>
                          </td>
                          <td className="td align-top text-muted-foreground">{v.originalDisplay || '—'}</td>
                          <td className="td align-top">
                            {v.inputType === 'Material' ? (
                              <SearchSelect
                                options={materialOptions}
                                value={d.value ? Number(d.value) : null}
                                onChange={(mid) => setD(v.valueId, { value: mid ? String(mid) : '' })}
                                placeholder={v.originalMaterialName ?? 'Select material…'}
                              />
                            ) : v.inputType === 'YesNo' ? (
                              <select className="input" value={d.value} onChange={(e) => setD(v.valueId, { value: e.target.value })}>
                                <option value="">{v.originalValue ? `(${v.originalValue})` : '—'}</option>
                                <option value="Yes">Yes</option>
                                <option value="No">No</option>
                              </select>
                            ) : (
                              <input
                                className="input"
                                inputMode={v.inputType === 'Number' ? 'decimal' : undefined}
                                value={d.value}
                                placeholder={v.originalValue ?? ''}
                                maxLength={4000}
                                onChange={(e) => setD(v.valueId, { value: e.target.value })}
                              />
                            )}
                          </td>
                          <td className="td align-top">
                            <input className="input" inputMode="decimal" value={d.min} disabled={!rangeable} onChange={(e) => setD(v.valueId, { min: e.target.value })} />
                          </td>
                          <td className="td align-top">
                            <input className="input" inputMode="decimal" value={d.max} disabled={!rangeable} onChange={(e) => setD(v.valueId, { max: e.target.value })} />
                          </td>
                          <td className="td align-top">
                            {changed && (
                              <button className="btn-icon h-7 w-7" title="Reset to the step value" onClick={() => setD(v.valueId, { value: '', min: '', max: '' })}>
                                <RotateCcw className="h-3.5 w-3.5" />
                              </button>
                            )}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div>
            <button className="btn-ghost btn-sm -ml-2" onClick={() => setShowPasses((s) => !s)}>
              {showPasses ? <ChevronLeft className="h-3.5 w-3.5 -rotate-90" /> : <ChevronRight className="h-3.5 w-3.5" />} Sub steps ({q.data.passes.filter((p) => p.filled).length} of {q.data.passes.length} filled)
            </button>
            {showPasses && (
              <div className="mt-2">
                <PassList passes={q.data.passes} compact />
              </div>
            )}
          </div>
        </div>
      ) : null}
    </Modal>
  )
}
