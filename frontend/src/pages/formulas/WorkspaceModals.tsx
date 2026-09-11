import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, History, XCircle } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { dateTimeSeconds, num } from '@/lib/format'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { waitForCommand } from './deviceCommands'
import { parseNum } from './formulaMath'
import type { CommandStatus, HistoryRow } from './types'

/** "Confirmation: Nozzle Cleaning Completed" — shown before dispensing once the clean-nozzle interval has passed. */
export function NozzleConfirmModal({ open, onClose, groupId, onConfirmed }: {
  open: boolean
  onClose: () => void
  groupId: number
  onConfirmed: () => void
}) {
  const toast = useToast()
  const confirm = useMutation({
    mutationFn: () => api.post<{ message: string }>('/dispensing/settings/nozzle-cleaned', { groupId }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      onConfirmed()
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Confirmation: Nozzle Cleaning Completed"
      size="sm"
      footer={
        <button className="btn-primary" onClick={() => confirm.mutate()} disabled={confirm.isPending}>
          {confirm.isPending && <Spinner />} Confirm
        </button>
      }
    >
      <p className="text-sm">
        Please click the &quot;Confirm&quot; button to indicate that you have completed the cleaning of the nozzle. Once confirmed, you will be able to dispense.
      </p>
    </Modal>
  )
}

/** "Recalc Amount (Grams)" — manual weight for Calc Batch when no scale is selected. */
export function RecalcAmountModal({ open, onClose, onSubmit, busy, materialName }: {
  open: boolean
  onClose: () => void
  onSubmit: (grams: number) => void
  busy?: boolean
  materialName?: string
}) {
  const [value, setValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (open) {
      setValue('')
      setError(null)
    }
  }, [open])
  const submit = () => {
    const g = parseNum(value)
    if (g === null || g <= 0) return setError('Please enter a valid recalc amount in grams!')
    onSubmit(g)
  }
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Recalc Amount (Grams)"
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={busy}>
            {busy && <Spinner />} Submit
          </button>
        </>
      }
    >
      <p className="mb-3 text-sm text-muted-foreground">No connected scale was selected. Please enter gram amount.</p>
      <ErrorBanner message={error} />
      <Field label="Gram Amount" hint={materialName ? `Weighed amount of ${materialName}; every ingredient is recalculated from it.` : undefined}>
        <input className="input tabular-nums" inputMode="decimal" autoFocus value={value} onChange={(e) => setValue(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
      </Field>
    </Modal>
  )
}

/** "Select Location (Optional)" before Record & Reset. */
export function SelectLocationModal({ open, locations, onClose, onSave, busy }: {
  open: boolean
  locations: { id: number; name: string }[]
  onClose: () => void
  onSave: (locationId: number) => void
  busy?: boolean
}) {
  const [value, setValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (open) {
      setValue('')
      setError(null)
    }
  }, [open])
  const save = () => {
    if (!value) return setError('Location is required when recording a dispense. Please select a location from the dropdown above!')
    onSave(Number(value))
  }
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Select Location (Optional)"
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-primary" onClick={save} disabled={busy}>
            {busy && <Spinner />} Save
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <select className="input" value={value} onChange={(e) => setValue(e.target.value)} aria-label="Location">
        <option value="">Please select a location...</option>
        {locations.map((l) => (
          <option key={l.id} value={l.id}>
            {l.name}
          </option>
        ))}
      </select>
      <p className="mt-2 text-xs text-muted-foreground">The dispensed amounts are deducted from the selected batches at this location.</p>
    </Modal>
  )
}

/** "View History" with the legacy columns. */
export function FormulaHistoryModal({ open, onClose, formulaId, title }: { open: boolean; onClose: () => void; formulaId: number; title?: string }) {
  const q = useQuery({
    queryKey: ['formula-history', formulaId],
    queryFn: () => api.get<HistoryRow[]>(`/formulas/${formulaId}/history`).then((r) => r.data),
    enabled: open && formulaId > 0,
  })
  return (
    <Modal open={open} onClose={onClose} title={title ?? 'History'} size="full" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : !q.data?.length ? (
        <EmptyState title="No Data to display" icon={<History className="h-5 w-5" />} />
      ) : (
        <div className="max-h-[65vh] overflow-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="sticky top-0 border-b bg-muted">
              <tr>
                <th className="th">UserName</th>
                <th className="th">Field</th>
                <th className="th">Description</th>
                <th className="th text-right">OldValue</th>
                <th className="th text-right">ValueAdded</th>
                <th className="th text-right">NewValue</th>
                <th className="th">Employee Name</th>
                <th className="th">Date</th>
              </tr>
            </thead>
            <tbody>
              {q.data.map((r) => (
                <tr key={r.id} className="border-b align-top last:border-0">
                  <td className="td">{r.userName || '—'}</td>
                  <td className="td font-medium">{r.field}</td>
                  <td className="td max-w-[28rem] whitespace-pre-line">{r.description}</td>
                  <td className="td text-right tabular-nums">{r.oldValue}</td>
                  <td className="td text-right tabular-nums">{r.valueAdded}</td>
                  <td className="td text-right tabular-nums">{r.newValue}</td>
                  <td className="td">{r.employeeName}</td>
                  <td className="td whitespace-nowrap">{dateTimeSeconds(r.date)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  )
}

export interface DispenseMaterial {
  key: string
  productCode?: string | null
  productName: string
  grams: number
  colorCode?: string | null
}

/**
 * "Dispensing" — the can-filling animation of the legacy page while the dispense machine (via the network bridge) works.
 * Polls the queued command until the bridge reports the result.
 */
export function DispensingModal({ commandId, materials, onClose, onFinished }: {
  commandId: number | null
  materials: DispenseMaterial[]
  onClose: () => void
  onFinished: (status: CommandStatus) => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [status, setStatus] = useState<CommandStatus | null>(null)
  const cancelled = useRef(false)
  const finished = useRef(onFinished)
  finished.current = onFinished

  useEffect(() => {
    if (!commandId) return
    cancelled.current = false
    setStatus(null)
    waitForCommand(commandId, { timeoutMs: 15 * 60_000, intervalMs: 1500, onUpdate: setStatus, isCancelled: () => cancelled.current })
      .then((s) => {
        if (cancelled.current) return
        setStatus(s)
        finished.current(s)
      })
      .catch((e) => toast.error(errorMessage(e)))
    return () => {
      cancelled.current = true
    }
  }, [commandId, toast])

  const cancel = useMutation({
    mutationFn: () => api.post<{ message: string }>(`/dispensing/commands/${commandId}/cancel`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formula-workspace'] })
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const done = !!status?.done
  const ok = status?.status === 'Succeeded'
  const total = materials.reduce((s, m) => s + m.grams, 0) || 1
  const fill = ok ? 100 : status?.status === 'Sent' ? 70 : 12
  const message = !status || status.status === 'Pending'
    ? 'Preparing..... waiting for the network bridge'
    : status.status === 'Sent'
      ? 'Dispensing…'
      : ok
        ? 'Dispensing completed.'
        : status.message || `Dispensing ${status.status.toLowerCase()}.`

  return (
    <Modal
      open={!!commandId}
      onClose={() => (done ? onClose() : undefined)}
      title="Dispensing"
      size="lg"
      footer={
        done ? (
          <button className="btn-primary" onClick={onClose}>
            Close
          </button>
        ) : (
          <button className="btn-secondary" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
            {cancel.isPending && <Spinner />} Cancel Dispense
          </button>
        )
      }
    >
      <div className="grid gap-5 sm:grid-cols-[1fr_auto]">
        <ul className="max-h-56 space-y-1 overflow-y-auto text-sm">
          {materials.map((m) => (
            <li key={m.key} className="flex items-center gap-2 whitespace-nowrap">
              <span className="inline-block h-3 w-3 shrink-0 rounded-sm border" style={{ backgroundColor: m.colorCode || '#000000' }} />
              <span className="truncate">
                {m.productCode} - ({m.productName})
              </span>
              <span className="ml-auto tabular-nums text-muted-foreground">{num(m.grams, 4)} g</span>
            </li>
          ))}
        </ul>
        {/* The can: colour layers proportional to the grams of each tint, filling up while dispensing. */}
        <div className="mx-auto flex flex-col items-center">
          <div className="relative h-56 w-28 overflow-hidden rounded-b-[2.5rem] rounded-t-[1rem] border-2 border-slate-400 bg-slate-100 dark:bg-slate-800">
            <div className="absolute inset-x-0 bottom-0 flex flex-col-reverse transition-[height] duration-[1500ms] ease-linear" style={{ height: `${fill}%` }}>
              {materials.map((m) => (
                <div key={m.key} style={{ height: `${(m.grams / total) * 100}%`, backgroundColor: m.colorCode || '#000000' }} className="w-full border-t border-white/20" />
              ))}
            </div>
            {!done && <div className="absolute left-1/2 top-0 h-full w-1 -translate-x-1/2 animate-pulse bg-slate-700/60" />}
          </div>
        </div>
      </div>
      <div className={clsx('mt-4 flex items-center justify-center gap-2 text-sm font-medium', done ? (ok ? 'text-success' : 'text-destructive') : 'text-muted-foreground')}>
        {!done ? <Spinner /> : ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
        <span className="whitespace-pre-line">{message}</span>
      </div>
    </Modal>
  )
}
