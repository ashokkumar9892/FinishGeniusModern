import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { CheckCircle2, CircleDashed, Copy } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { SearchSelect } from '@/components/SearchSelect'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import type { MaterialOption, ScheduleRow, StepPassView, StepView } from './types'

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

const LEAVE_MESSAGE = 'You have unsaved changes. Leave this page and discard them?'

/**
 * Warns before losing unsaved changes: browser reload/close (beforeunload) and in-app link clicks
 * (sidebar, breadcrumbs). The app uses a plain BrowserRouter, so route blocking is done by
 * intercepting anchor clicks in the capture phase before React Router handles them.
 */
export function useUnsavedGuard(dirty: boolean) {
  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault()
      e.returnValue = ''
    }
    const onClick = (e: MouseEvent) => {
      const a = (e.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null
      if (!a || a.target === '_blank' || a.hasAttribute('download') || e.defaultPrevented) return
      if (a.origin !== location.origin) return
      if (!window.confirm(LEAVE_MESSAGE)) {
        e.preventDefault()
        e.stopPropagation()
      }
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    document.addEventListener('click', onClick, true)
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload)
      document.removeEventListener('click', onClick, true)
    }
  }, [dirty])
}

/** Asks for confirmation when there are unsaved changes (for buttons such as Back). */
export const confirmLeave = (dirty: boolean) => !dirty || window.confirm(LEAVE_MESSAGE)

export function useMaterials(groupId: number, params: { categoryId?: number | null; type?: number | null }, enabled = true) {
  return useQuery({
    queryKey: ['materials', groupId, params.type ?? null, params.categoryId ?? null],
    queryFn: () =>
      api
        .get<MaterialOption[]>('/materials', { params: { groupId, type: params.type ?? undefined, categoryId: params.categoryId ?? undefined } })
        .then((r) => r.data),
    enabled: enabled && groupId > 0 && (params.categoryId != null || params.type != null),
    staleTime: 60_000,
  })
}

// ---------------------------------------------------------------------------
// Bulk copy (steps and schedules)
// ---------------------------------------------------------------------------

export function BulkCopyModal({ open, onClose, count, onCopy }: {
  open: boolean
  onClose: () => void
  count: number
  /** Performs the copy and returns the server message. */
  onCopy: (destinationGroupId: number) => Promise<string>
}) {
  const { groups, groupId } = useGroup()
  const [dest, setDest] = useState<number | null>(groupId)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setDest(groupId)
      setError(null)
      setResult(null)
    }
  }, [open, groupId])

  const run = async () => {
    if (!dest) {
      setError('Select the destination group.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      setResult(await onCopy(dest))
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  if (result)
    return (
      <Modal open={open} onClose={onClose} title="Bulk Copy Results" size="sm" footer={<button className="btn-primary" onClick={onClose}>Close</button>}>
        <div className="flex items-start gap-3">
          <CheckCircle2 className="h-5 w-5 text-success shrink-0 mt-0.5" />
          <p className="text-sm">{result}</p>
        </div>
      </Modal>
    )

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Bulk Copying ${count} Items`}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={run} disabled={busy || !dest}>
            {busy ? <Spinner /> : <Copy className="h-4 w-4" />} Copy
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <Field label="Select the Destination Group" required>
        <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={dest} onChange={setDest} placeholder="Select group" clearable={false} />
      </Field>
      <p className="mt-3 text-xs text-muted-foreground">
        Categories, characteristics and materials used by the copied items are matched by name in the destination group and created when missing.
      </p>
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// View Step
// ---------------------------------------------------------------------------

/** Every sub step pass of a step: "Sub Step Not Filled" or its saved values. */
export function PassList({ passes, compact }: { passes: StepPassView[]; compact?: boolean }) {
  if (passes.length === 0) return <EmptyState title="This industry sector has no sub steps configured" />
  return (
    <ol className="divide-y rounded-md border">
      {passes.map((p) => (
        <li key={`${p.subStepId}-${p.pass}`} className={clsx('flex gap-3', compact ? 'px-3 py-2' : 'px-4 py-3')}>
          <span
            className={clsx(
              'mt-0.5 h-7 min-w-9 px-1.5 rounded-md grid place-items-center text-xs font-bold shrink-0',
              p.filled ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground',
            )}
          >
            {p.label}
          </span>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium text-sm">{p.name}</span>
              {p.userRole === 'Admin' && <span className="badge bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300">Admin</span>}
            </div>
            {!p.filled ? (
              <div className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
                <CircleDashed className="h-3.5 w-3.5" /> Sub Step Not Filled
              </div>
            ) : (
              <div className="mt-1 space-y-2">
                {p.entries
                  .filter((e) => e.categoryId != null)
                  .map((e) => (
                    <div key={e.entryId}>
                      <div className="text-xs text-muted-foreground">
                        {e.header && <span>{e.header}: </span>}
                        <span className="font-medium text-foreground">{e.categoryName}</span>
                      </div>
                      {e.values.filter((v) => v.display).length > 0 && (
                        <dl className="mt-1 grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-0.5 text-sm">
                          {e.values
                            .filter((v) => v.display)
                            .map((v) => (
                              <div key={v.valueId} className="flex gap-2 min-w-0">
                                <dt className="text-muted-foreground shrink-0">{v.characteristic}:</dt>
                                <dd className="font-medium truncate" title={v.display}>{v.display}</dd>
                              </div>
                            ))}
                        </dl>
                      )}
                    </div>
                  ))}
              </div>
            )}
          </div>
        </li>
      ))}
    </ol>
  )
}

export function ViewStepModal({ open, onClose, stepId, title }: { open: boolean; onClose: () => void; stepId: number | null; title?: string }) {
  const q = useQuery({
    queryKey: ['process-step-view', stepId],
    queryFn: () => api.get<StepView>(`/process-steps/${stepId}/view`).then((r) => r.data),
    enabled: open && !!stepId,
  })
  const filled = q.data?.passes.filter((p) => p.filled).length ?? 0
  return (
    <Modal open={open} onClose={onClose} size="lg" title={`View Step (${title ?? q.data?.name ?? '…'})`} footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : q.data ? (
        <>
          <div className="mb-3 flex flex-wrap gap-x-5 gap-y-1 text-xs text-muted-foreground">
            <span>Group: <b className="text-foreground">{q.data.groupName}</b></span>
            <span>Industry Sector: <b className="text-foreground">{q.data.industrySectorName}</b></span>
            <span>Filled: <b className="text-foreground">{filled} of {q.data.passes.length}</b></span>
            {q.data.updatedAt && <span>Updated {dateTime(q.data.updatedAt)}</span>}
          </div>
          <PassList passes={q.data.passes} />
        </>
      ) : null}
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Printing
// ---------------------------------------------------------------------------

/**
 * Renders children into a body-level container that is the ONLY thing printed while mounted
 * (the app root is hidden in print). `page` sets the @page size, e.g. "4in 2in" for labels.
 */
export function PrintPortal({ children, page }: { children: ReactNode; page?: string }) {
  return createPortal(
    <>
      <style>{`@media print {
  #root { display: none !important; }
  .fg-print-portal { display: block !important; }
  ${page ? `@page { size: ${page}; margin: 0; }` : '@page { margin: 12mm; }'}
}
.fg-print-portal { display: none; }`}</style>
      <div className="fg-print-portal">{children}</div>
    </>,
    document.body,
  )
}

// ---------------------------------------------------------------------------
// Process System filter (Material Quantities / Pricing)
// ---------------------------------------------------------------------------

export const scheduleLabel = (s: Pick<ScheduleRow, 'name' | 'groupName' | 'number'>) => `#${s.name} [${s.groupName}] - ${s.number}`

/** Selected schedule persisted in the URL (?schedule=). */
export function useScheduleParam(): [number | null, (id: number | null) => void] {
  const [params, setParams] = useSearchParams()
  const raw = Number(params.get('schedule') ?? 0)
  const value = raw > 0 ? raw : null
  const set = (id: number | null) =>
    setParams(
      (p) => {
        const next = new URLSearchParams(p)
        if (id) next.set('schedule', String(id))
        else next.delete('schedule')
        return next
      },
      { replace: true },
    )
  return [value, set]
}

export function useSchedules(groupId: number) {
  return useQuery({
    queryKey: ['process-schedules', groupId, false],
    queryFn: () => api.get<ScheduleRow[]>('/process-schedules', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
}

export function ScheduleFilter({ value, onChange }: { value: number | null; onChange: (id: number | null) => void }) {
  const { groupId } = useGroup()
  const q = useSchedules(groupId)
  const options = useMemo(
    () => (q.data ?? []).map((s) => ({ value: s.id, label: scheduleLabel(s), sub: s.customerName ?? undefined })),
    [q.data],
  )
  // A schedule from another group (group switched in the header) is cleared.
  useEffect(() => {
    if (value && q.data && !q.data.some((s) => s.id === value)) onChange(null)
  }, [value, q.data, onChange])
  return (
    <div className="card p-4 no-print">
      <Field label="Process System (Name or #)">
        <SearchSelect
          options={options}
          value={value}
          onChange={onChange}
          placeholder={q.isLoading ? 'Loading…' : 'Select a process system'}
          emptyText="No process schedules in this group"
        />
      </Field>
    </div>
  )
}

/** Large orange output figure (Square Footage, Total Price…). */
export function BigStat({ label, value, hint }: { label: string; value: ReactNode; hint?: ReactNode }) {
  return (
    <div className="rounded-lg border border-primary/25 bg-accent/60 px-4 py-3">
      <div className="text-xs font-semibold uppercase tracking-wide text-accent-foreground/80">{label}</div>
      <div className="mt-1 text-3xl font-bold text-primary tabular-nums">{value}</div>
      {hint && <div className="mt-0.5 text-xs text-muted-foreground">{hint}</div>}
    </div>
  )
}
