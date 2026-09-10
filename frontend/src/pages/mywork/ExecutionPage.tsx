import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { AxiosError } from 'axios'
import {
  AlertTriangle, Check, CheckCircle2, ChevronDown, ChevronsDownUp, ChevronsUpDown, HelpCircle, History, Lock, MessageSquare, Plus,
  TriangleAlert, Undo2, X,
} from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useMe } from '@/lib/auth'
import { dateTime, dateTimeSeconds } from '@/lib/format'
import { ConfirmDialog, EmptyState, LoadingBlock, Note, PageHeader, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import {
  ExecutionStatus, ProgressBar, StatusBadge, isOutOfRange, parseNumber, rangeText,
  type ApiMessage, type ExecutionCheck, type ExecutionDetail, type ExecutionLine,
} from './shared'
import { AddersModal, DefectsModal, ExecutionHistoryModal } from './executionParts'

interface CheckResponse extends ApiMessage {
  outOfRange: boolean
  check: ExecutionCheck
}

type ModalKind = 'adders' | 'defects' | 'history' | 'complete' | 'cancel' | null

/** Shop-floor checklist for one run of a process schedule (`/my-work/:id`). Large touch targets for tablets. */
export default function ExecutionPage() {
  const { id: idParam } = useParams()
  const id = Number(idParam)
  const me = useMe()
  const qc = useQueryClient()
  const toast = useToast()
  const navigate = useNavigate()
  const [modal, setModal] = useState<ModalKind>(null)
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set())
  const [busyLines, setBusyLines] = useState<Set<number>>(new Set())

  const key = ['execution', id] as const
  const q = useQuery({
    queryKey: key,
    queryFn: () => api.get<ExecutionDetail>(`/my-work/executions/${id}`).then((r) => r.data),
    enabled: id > 0,
    refetchInterval: 30_000, // several operators may work the same process
  })
  const ex = q.data

  const setBusy = (lineId: number, on: boolean) =>
    setBusyLines((s) => {
      const n = new Set(s)
      if (on) n.add(lineId)
      else n.delete(lineId)
      return n
    })

  const afterChange = () => {
    qc.invalidateQueries({ queryKey: key })
    qc.invalidateQueries({ queryKey: ['execution-history', id] })
    qc.invalidateQueries({ queryKey: ['executions'] })
  }

  const check = useMutation({
    mutationFn: ({ line, value }: { line: ExecutionLine; value?: string }) =>
      api.post<CheckResponse>(`/my-work/lines/${line.id}/check`, { recordedValue: value ?? null }).then((r) => r.data),
    onMutate: ({ line }) => setBusy(line.id, true),
    onSuccess: (res, { line }) => {
      // Show the stamp immediately, then reconcile with the server.
      qc.setQueryData<ExecutionDetail>(key, (old) =>
        old && {
          ...old,
          checkedLines: old.checkedLines + (line.checks.length === 0 ? 1 : 0),
          steps: old.steps.map((s) => ({
            ...s,
            lines: s.lines.map((l) => (l.id === line.id ? { ...l, checks: [...l.checks, res.check], outOfRange: res.outOfRange } : l)),
          })),
        },
      )
      if (res.outOfRange) toast.error(res.message)
      afterChange()
    },
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: (_r, _e, { line }) => setBusy(line.id, false),
  })

  const undo = useMutation({
    mutationFn: ({ checkId }: { checkId: number; lineId: number }) => api.delete<ApiMessage>(`/my-work/checks/${checkId}`).then((r) => r.data),
    onMutate: ({ lineId }) => setBusy(lineId, true),
    onSuccess: (res) => {
      toast.success(res.message)
      afterChange()
    },
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: (_r, _e, { lineId }) => setBusy(lineId, false),
  })

  const finish = useMutation({
    mutationFn: (kind: 'complete' | 'cancel') => api.post<ApiMessage>(`/my-work/executions/${id}/${kind}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setModal(null)
      afterChange()
      qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
      qc.invalidateQueries({ queryKey: ['dashboard-departments'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const allSteps = useMemo(() => ex?.steps.map((s) => s.stepNumber) ?? [], [ex])

  if (!(id > 0)) return <EmptyState title="Process not found" action={<Link className="btn-secondary" to="/my-work">Back to My Work</Link>} />
  if (q.isLoading) return <LoadingBlock label="Loading process…" />
  if (q.isError || !ex) {
    const status = (q.error as AxiosError | null)?.response?.status
    return (
      <>
        <PageHeader title="Process" breadcrumbs={[['My Work', '/my-work#workProgress'], 'Process']} />
        <EmptyState
          title={status === 404 ? 'Process not found' : status === 403 ? 'Access denied' : 'Could not load this process'}
          description={errorMessage(q.error)}
          action={<Link className="btn-secondary" to="/my-work#workProgress">Back to My Work</Link>}
        />
      </>
    )
  }

  const readOnly = ex.status !== ExecutionStatus.InProgress
  const subject = `My Work #${ex.id} — ${ex.scheduleName}`
  const unchecked = ex.totalLines - ex.checkedLines
  const bigBtn = 'h-11 px-4 text-sm'

  return (
    <div className="pb-24 sm:pb-6">
      <PageHeader
        title={`${ex.scheduleName} (#${ex.scheduleNumber})`}
        breadcrumbs={[['My Work', '/my-work#workProgress'], 'Process']}
        subtitle={
          <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
            <StatusBadge status={ex.status} />
            <span>{ex.groupName}</span>
            <span aria-hidden>·</span>
            <span>
              Process #{ex.id} started {dateTime(ex.startedAt)} by {ex.userName}
            </span>
            {ex.customerName && (
              <>
                <span aria-hidden>·</span>
                <span>{ex.customerName}</span>
              </>
            )}
          </span>
        }
      />

      {/* Toolbar */}
      <div className="flex flex-wrap gap-2 mb-4 no-print">
        <button className={clsx('btn-secondary', bigBtn)} onClick={() => setModal('adders')}>
          <Plus className="h-4 w-4" /> Adders <span className="badge bg-muted text-foreground">{ex.adderCount}</span>
        </button>
        <button className={clsx('btn-secondary', bigBtn)} onClick={() => navigate(`/messages?compose=Internal&subject=${encodeURIComponent(subject)}`)}>
          <MessageSquare className="h-4 w-4" /> Internal DPM
        </button>
        <button className={clsx('btn-secondary', bigBtn)} onClick={() => navigate(`/messages?compose=Question&subject=${encodeURIComponent(subject)}`)}>
          <HelpCircle className="h-4 w-4" /> AWFI Finishing Question Support
        </button>
        <button className={clsx('btn-secondary', bigBtn)} onClick={() => setModal('defects')}>
          <AlertTriangle className="h-4 w-4 text-destructive" /> Defects{' '}
          <span className={clsx('badge', ex.defectCount > 0 ? 'bg-destructive text-destructive-foreground' : 'bg-muted text-foreground')}>{ex.defectCount}</span>
        </button>
        <button className={clsx('btn-secondary', bigBtn)} onClick={() => setModal('history')}>
          <History className="h-4 w-4" /> View History
        </button>
        {!readOnly && (
          <div className="flex gap-2 sm:ml-auto w-full sm:w-auto">
            <button className={clsx('btn-success flex-1 sm:flex-none', bigBtn)} onClick={() => setModal('complete')}>
              <CheckCircle2 className="h-4 w-4" /> Complete process
            </button>
            <button className={clsx('btn-danger flex-1 sm:flex-none', bigBtn)} onClick={() => setModal('cancel')}>
              <X className="h-4 w-4" /> Cancel process
            </button>
          </div>
        )}
      </div>

      {readOnly && (
        <div className="mb-4">
          <Note tone="info">
            <span className="inline-flex items-center gap-1.5 font-medium">
              <Lock className="h-3.5 w-3.5" /> This process was {ex.status === ExecutionStatus.Completed ? 'completed' : 'cancelled'}
              {ex.completedAt ? ` on ${dateTime(ex.completedAt)}` : ''}. The checklist is read-only.
            </span>
            {ex.status === ExecutionStatus.Completed && ex.checklistDeletionEnabled && ex.checkedLines === 0 && (
              <span className="block mt-0.5">Checklist marks were cleared on completion ("Enable Checklist Deletion on Submit" is on for this group). The full record is kept in View History.</span>
            )}
          </Note>
        </div>
      )}

      {/* Overall progress */}
      <div className="card p-4 mb-4">
        <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
          <div className="text-sm font-semibold">
            {ex.checkedLines} of {ex.totalLines} lines checked
            {!readOnly && unchecked > 0 && <span className="ml-2 font-normal text-muted-foreground">({unchecked} to go)</span>}
          </div>
          <div className="flex gap-1">
            <button className="btn-ghost btn-sm" onClick={() => setCollapsed(new Set())}>
              <ChevronsUpDown className="h-3.5 w-3.5" /> Expand all
            </button>
            <button className="btn-ghost btn-sm" onClick={() => setCollapsed(new Set(allSteps))}>
              <ChevronsDownUp className="h-3.5 w-3.5" /> Collapse all
            </button>
          </div>
        </div>
        <ProgressBar done={ex.checkedLines} total={ex.totalLines} size="lg" />
      </div>

      {ex.steps.length === 0 ? (
        <div className="card">
          <EmptyState title="This process has no checklist lines" />
        </div>
      ) : (
        <div className="space-y-4">
          {ex.steps.map((s) => {
            const done = s.lines.filter((l) => l.checks.length > 0).length
            const isCollapsed = collapsed.has(s.stepNumber)
            return (
              <section key={s.stepNumber} className="card overflow-hidden">
                <button
                  type="button"
                  className="w-full flex items-center gap-3 px-4 py-3 text-left bg-muted/50 hover:bg-muted border-b"
                  aria-expanded={!isCollapsed}
                  onClick={() =>
                    setCollapsed((c) => {
                      const n = new Set(c)
                      if (n.has(s.stepNumber)) n.delete(s.stepNumber)
                      else n.add(s.stepNumber)
                      return n
                    })
                  }
                >
                  <ChevronDown className={clsx('h-5 w-5 shrink-0 transition-transform text-muted-foreground', isCollapsed && '-rotate-90')} />
                  <h2 className="flex-1 min-w-0 text-base sm:text-lg font-semibold truncate">
                    #{s.stepNumber} {s.stepName}
                  </h2>
                  {done === s.lines.length && <CheckCircle2 className="h-5 w-5 text-success shrink-0" aria-label="Step complete" />}
                  <ProgressBar done={done} total={s.lines.length} className="w-28 sm:w-40" />
                </button>
                {!isCollapsed && (
                  <ul>
                    {s.lines.map((l, i) => (
                      <LineRow
                        key={l.id}
                        line={l}
                        striped={i % 2 === 1}
                        readOnly={readOnly}
                        busy={busyLines.has(l.id)}
                        canUndo={(c) => c.userId === me.id || ex.canUndoAny}
                        onCheck={(value) => check.mutate({ line: l, value })}
                        onUndo={(c) => undo.mutate({ checkId: c.id, lineId: l.id })}
                      />
                    ))}
                  </ul>
                )}
              </section>
            )
          })}
        </div>
      )}

      <AddersModal open={modal === 'adders'} onClose={() => setModal(null)} executionId={ex.id} groupId={ex.groupId} readOnly={readOnly} />
      <DefectsModal open={modal === 'defects'} onClose={() => setModal(null)} executionId={ex.id} groupId={ex.groupId} readOnly={readOnly} />
      <ExecutionHistoryModal open={modal === 'history'} onClose={() => setModal(null)} executionId={ex.id} />

      <ConfirmDialog
        open={modal === 'complete' || modal === 'cancel'}
        title={modal === 'complete' ? 'Complete Process' : 'Cancel Process'}
        danger={modal === 'cancel'}
        confirmLabel={modal === 'complete' ? 'Complete process' : 'Cancel process'}
        busy={finish.isPending}
        onClose={() => setModal(null)}
        onConfirm={() => (modal === 'complete' || modal === 'cancel') && finish.mutate(modal)}
        message={
          modal === 'complete' ? (
            <>
              Are you sure you want to complete "{ex.scheduleName}"?
              {unchecked > 0 && (
                <span className="block mt-2 text-amber-700 dark:text-amber-400">
                  {unchecked} of {ex.totalLines} checklist lines are not checked yet.
                </span>
              )}
              {ex.checklistDeletionEnabled && (
                <span className="block mt-2 text-muted-foreground">This group clears the checklist marks on completion; the history is kept.</span>
              )}
            </>
          ) : (
            <>Are you sure you want to cancel "{ex.scheduleName}"? The checklist becomes read-only.</>
          )
        }
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// One checklist line
// ---------------------------------------------------------------------------

function LineRow({ line, striped, readOnly, busy, canUndo, onCheck, onUndo }: {
  line: ExecutionLine
  striped: boolean
  readOnly: boolean
  busy: boolean
  canUndo: (c: ExecutionCheck) => boolean
  onCheck: (value?: string) => void
  onUndo: (c: ExecutionCheck) => void
}) {
  const [prompt, setPrompt] = useState(false)
  const [showAll, setShowAll] = useState(false)
  const count = line.checks.length
  const last = count > 0 ? line.checks[count - 1] : undefined
  const checked = count > 0
  const hasRange = line.minValue != null || line.maxValue != null
  const range = rangeText(line.minValue, line.maxValue, line.unit)

  const onToggle = () => {
    if (readOnly || busy) return
    if (hasRange) setPrompt((p) => !p)
    else onCheck()
  }

  return (
    <li className={clsx('relative flex items-start gap-3 sm:gap-4 px-3 sm:px-4 py-3 border-b last:border-0', striped ? 'bg-muted/40' : 'bg-card')}>
      <div className="relative shrink-0">
        <button
          type="button"
          onClick={onToggle}
          disabled={readOnly || busy}
          aria-label={checked ? `Record another check for ${line.description} (checked ${count} time${count === 1 ? '' : 's'})` : `Check ${line.description}`}
          title={readOnly ? undefined : checked ? 'Tap to record another check' : 'Tap to check'}
          className={clsx(
            'h-14 w-14 rounded-lg border-2 grid place-items-center transition-all focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-ring/40',
            checked ? 'bg-success border-success text-white shadow-sm' : 'bg-red-600 border-red-700 hover:bg-red-500',
            !readOnly && !busy && 'active:scale-95 cursor-pointer',
            readOnly && 'opacity-80 cursor-default',
          )}
        >
          {busy ? <Spinner className="h-6 w-6 text-white" /> : checked ? <Check className="h-9 w-9" strokeWidth={3} /> : null}
        </button>
        {checked && (
          <span className="absolute -top-2 -right-2 min-w-[22px] h-[22px] px-1 rounded-full bg-slate-900 text-white text-xs font-bold grid place-items-center ring-2 ring-card dark:bg-white dark:text-slate-900">
            {count}
          </span>
        )}
        {prompt && (
          <ValuePrompt
            line={line}
            onClose={() => setPrompt(false)}
            onSave={(v) => {
              setPrompt(false)
              onCheck(v)
            }}
          />
        )}
      </div>

      <div className="flex-1 min-w-0 pt-0.5">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium text-[15px] leading-snug break-words">{line.description}</span>
          {line.value && <span className="badge bg-slate-200 text-slate-800 dark:bg-slate-700 dark:text-slate-100 text-[13px] px-2.5 py-1">{line.value}</span>}
          {range && <span className="text-xs text-muted-foreground">Range {range}</span>}
          {line.outOfRange && (
            <span className="badge bg-amber-100 text-amber-900 border border-amber-300 gap-1">
              <TriangleAlert className="h-3.5 w-3.5" /> Out of tolerance
            </span>
          )}
        </div>
        {last && (
          <div className="mt-1 text-xs text-muted-foreground">
            <span className="text-success font-medium">✓</span> {dateTimeSeconds(last.checkedAt)} · {last.userName}
            {last.recordedValue && (
              <span className="ml-1">
                · recorded <span className="font-semibold text-foreground">{last.recordedValue}{line.unit && line.unit !== '-' ? ` ${line.unit}` : ''}</span>
              </span>
            )}
            {count > 1 && (
              <button type="button" className="ml-2 underline underline-offset-2 hover:text-foreground" onClick={() => setShowAll((s) => !s)}>
                {showAll ? 'hide' : `${count - 1} earlier`}
              </button>
            )}
          </div>
        )}
        {showAll && count > 1 && (
          <ol className="mt-1 space-y-0.5 text-xs text-muted-foreground">
            {line.checks.slice(0, -1).reverse().map((c) => (
              <li key={c.id}>
                {dateTimeSeconds(c.checkedAt)} · {c.userName}
                {c.recordedValue && ` · ${c.recordedValue}`}
                {isOutOfRange(c.recordedValue, line.minValue, line.maxValue) && <span className="ml-1 text-amber-700">(out of tolerance)</span>}
              </li>
            ))}
          </ol>
        )}
      </div>

      {!readOnly && last && canUndo(last) && (
        <button type="button" className="btn-ghost btn-sm h-10 shrink-0 text-muted-foreground" disabled={busy} onClick={() => onUndo(last)} title="Undo the last check">
          <Undo2 className="h-4 w-4" /> <span className="hidden sm:inline">Undo</span>
        </button>
      )}
    </li>
  )
}

/** Small popover asking for the measured value of a line with an allowed range. */
function ValuePrompt({ line, onSave, onClose }: { line: ExecutionLine; onSave: (v: string) => void; onClose: () => void }) {
  const [value, setValue] = useState('')
  const ref = useRef<HTMLDivElement>(null)
  const n = parseNumber(value)
  const out = n != null && isOutOfRange(value, line.minValue, line.maxValue)

  useEffect(() => {
    const onDoc = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && onClose()
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
    }
  }, [onClose])

  return (
    <div ref={ref} className="absolute left-0 top-16 z-30 w-72 card shadow-xl p-3" role="dialog" aria-label="Record value">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (n != null) onSave(value.trim())
        }}
      >
        <label className="label" htmlFor={`val-${line.id}`}>
          Recorded value{line.unit && line.unit !== '-' ? ` (${line.unit})` : ''}
        </label>
        <input id={`val-${line.id}`} className={clsx('input h-11 text-base', out && 'border-amber-500')} inputMode="decimal" autoFocus value={value} onChange={(e) => setValue(e.target.value)} placeholder={line.value ?? ''} />
        <div className="mt-1 text-xs text-muted-foreground">Allowed range {rangeText(line.minValue, line.maxValue, line.unit)}</div>
        {out && (
          <div className="mt-2 flex items-center gap-1.5 text-xs font-medium text-amber-800 dark:text-amber-400">
            <TriangleAlert className="h-3.5 w-3.5" /> Out of tolerance — it will be saved and flagged.
          </div>
        )}
        {value.trim() !== '' && n == null && <div className="mt-2 text-xs text-destructive">Enter a number.</div>}
        <div className="mt-3 flex justify-end gap-2">
          <button type="button" className="btn-secondary h-10" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn-success h-10" disabled={n == null}>
            <Check className="h-4 w-4" /> Save check
          </button>
        </div>
      </form>
    </div>
  )
}
