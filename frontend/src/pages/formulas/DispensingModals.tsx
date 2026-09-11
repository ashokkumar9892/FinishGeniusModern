import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { format } from 'date-fns'
import { History, Save, Search, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { dateTime } from '@/lib/format'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { waitForCommand } from './deviceCommands'
import type { DispenseSettings } from './types'

const today = () => format(new Date(), 'yyyy-MM-dd')
const HOURS = Array.from({ length: 24 }, (_, i) => i + 1)

/** "Clean Nozzle": interval after the last dispense before the nozzle must be cleaned (legacy GroupDispenseTimeSpanMapping). */
export function CleanNozzleModal({ open, onClose, groupId }: { open: boolean; onClose: () => void; groupId: number }) {
  const toast = useToast()
  const qc = useQueryClient()
  const [hours, setHours] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const q = useQuery({
    queryKey: ['dispense-settings', groupId],
    queryFn: () => api.get<DispenseSettings>('/dispensing/settings', { params: { groupId } }).then((r) => r.data),
    enabled: open && groupId > 0,
  })
  useEffect(() => {
    if (open && q.data) setHours(q.data.cleanNozzleHours)
    if (open) setError(null)
  }, [open, q.data])

  const save = useMutation({
    mutationFn: () => api.put<{ message: string }>('/dispensing/settings', { groupId, cleanNozzleHours: hours }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['dispense-settings'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Clean Nozzle"
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={() => save.mutate()} disabled={save.isPending || q.isLoading}>
            {save.isPending && <Spinner />} Submit
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      {q.isLoading ? (
        <LoadingBlock />
      ) : (
        <>
          <Field label="Please select a time interval for cleaning the nozzle after the last dispense hour:">
            <select className="input" value={hours} onChange={(e) => setHours(Number(e.target.value))}>
              <option value={0}>None</option>
              {HOURS.map((h) => (
                <option key={h} value={h}>
                  {h === 1 ? '1 hour' : `${h} hours`}
                </option>
              ))}
            </select>
          </Field>
          <p className="mt-3 text-xs text-muted-foreground">
            When this time has passed since the last dispense, users must confirm that the nozzle was cleaned before they can dispense again.
            {q.data?.lastDispensedAt && <> Last dispense: {dateTime(q.data.lastDispensedAt)}.</>}
          </p>
        </>
      )}
    </Modal>
  )
}

interface PurgeBridge {
  id: number
  name: string
  lastSeenAt?: string | null
  online: boolean
  fromDate?: string | null
  toDate?: string | null
  time?: string | null
  dispensers: { id: number; name: string; canisters: number }[]
  activeFailures: number
}

type Draft = { fromDate: string; toDate: string; time: string }

/** "Purge" — the group's network bridges: automatic purge window (From / To / Time) and a manual purge of every filled canister. */
export function PurgeModal({ open, onClose, groupId }: { open: boolean; onClose: () => void; groupId: number }) {
  const toast = useToast()
  const qc = useQueryClient()
  const [drafts, setDrafts] = useState<Record<number, Draft>>({})
  const [status, setStatus] = useState<Record<number, string>>({})
  const [error, setError] = useState<string | null>(null)
  const q = useQuery({
    queryKey: ['purge-devices', groupId],
    queryFn: () => api.get<PurgeBridge[]>('/dispensing/purge', { params: { groupId } }).then((r) => r.data),
    enabled: open && groupId > 0,
  })
  useEffect(() => {
    if (!open || !q.data) return
    setDrafts(Object.fromEntries(q.data.map((b) => [b.id, { fromDate: b.fromDate ?? '', toDate: b.toDate ?? '', time: b.time ?? '' }])))
    setStatus({})
    setError(null)
  }, [open, q.data])

  const save = useMutation({
    mutationFn: (id: number) => api.put<{ message: string }>(`/dispensing/purge/${id}/settings`, drafts[id]),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['purge-devices'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const purge = useMutation({
    mutationFn: (id: number) => api.post<{ message: string; commandId: number }>(`/dispensing/purge/${id}/run`),
    onSuccess: (res, id) => {
      toast.success(res.data.message)
      setStatus((s) => ({ ...s, [id]: 'Purge requested — waiting for the network bridge…' }))
      waitForCommand(res.data.commandId, { timeoutMs: 120_000, intervalMs: 2000 })
        .then((r) => {
          const text = r.status === 'Succeeded' ? `Purge completed${r.message ? `: ${r.message}` : '.'}` : `Purge failed: ${r.message ?? r.status}`
          setStatus((s) => ({ ...s, [id]: text }))
          if (r.status === 'Succeeded') toast.success(text)
          else toast.error(text)
        })
        .catch(() => undefined)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const setDraft = (id: number, patch: Partial<Draft>) => setDrafts((d) => ({ ...d, [id]: { ...d[id], ...patch } }))

  return (
    <Modal open={open} onClose={onClose} title="Devices" size="xl" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      <ErrorBanner message={error} />
      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : !q.data?.length ? (
        <EmptyState title="No network bridges" description="Add a Network Bridge and connect the Dispense Machine to it in Dashboard › Devices." />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/60">
              <tr>
                <th className="th">Device Name</th>
                <th className="th">From Date</th>
                <th className="th">TO Date</th>
                <th className="th">Time</th>
                <th className="th text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {q.data.map((b) => {
                const d = drafts[b.id] ?? { fromDate: '', toDate: '', time: '' }
                return (
                  <tr key={b.id} className="border-b align-top last:border-0">
                    <td className="td min-w-[12rem]">
                      <div className="flex items-center gap-2 font-medium">
                        {b.name}
                        <span className={clsx('badge', b.online ? 'bg-emerald-100 text-emerald-800' : 'bg-muted text-muted-foreground')} title={b.lastSeenAt ? `Last seen ${dateTime(b.lastSeenAt)}` : 'Never connected'}>
                          {b.online ? 'Online' : 'Offline'}
                        </span>
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {b.dispensers.length ? b.dispensers.map((x) => `${x.name} (${x.canisters} canisters)`).join(', ') : 'No dispense machine connected'}
                      </div>
                      {b.activeFailures > 0 && <div className="mt-1 text-xs text-destructive">{b.activeFailures} failed purge(s) — please manually purge before dispensing.</div>}
                      {status[b.id] && <div className="mt-1 text-xs text-primary">{status[b.id]}</div>}
                    </td>
                    <td className="td">
                      <input type="date" className="input min-w-[9rem]" min={today()} value={d.fromDate} onChange={(e) => setDraft(b.id, { fromDate: e.target.value })} aria-label="From Date" />
                    </td>
                    <td className="td">
                      <input type="date" className="input min-w-[9rem]" min={today()} value={d.toDate} onChange={(e) => setDraft(b.id, { toDate: e.target.value })} aria-label="To Date" />
                    </td>
                    <td className="td">
                      <input type="time" className="input min-w-[7rem]" value={d.time} onChange={(e) => setDraft(b.id, { time: e.target.value })} aria-label="Time" />
                    </td>
                    <td className="td">
                      <div className="flex justify-end gap-1.5">
                        <button className="btn-primary btn-sm" disabled={purge.isPending && purge.variables === b.id} onClick={() => { setError(null); purge.mutate(b.id) }}>
                          {purge.isPending && purge.variables === b.id ? <Spinner /> : <Trash2 className="h-3.5 w-3.5" />} Purge
                        </button>
                        <button
                          className="btn-secondary btn-sm"
                          disabled={save.isPending && save.variables === b.id}
                          onClick={() => {
                            setError(null)
                            if (!d.fromDate) return setError('From Date is required.')
                            if (!d.toDate) return setError('To Date is required.')
                            if (d.fromDate > d.toDate) return setError('From Date must be less than or equal to To Date.')
                            if (!d.time) return setError('Time is required.')
                            save.mutate(b.id)
                          }}
                        >
                          {save.isPending && save.variables === b.id ? <Spinner /> : <Save className="h-3.5 w-3.5" />} Save
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <p className="mt-3 text-xs text-muted-foreground">
            Purge commands are picked up by the network bridge (it polls the server with its API key); purge results appear in Purge History.
          </p>
        </div>
      )}
    </Modal>
  )
}

interface PurgeHistoryRow {
  key: string
  groupName: string
  bridgeName: string
  purgeType: string
  canisterNumber: number
  date: string
  message?: string | null
  result: string
}

/** "Purge History" (administrators): purge results between two dates. */
export function PurgeHistoryModal({ open, onClose, groupId }: { open: boolean; onClose: () => void; groupId: number }) {
  const [from, setFrom] = useState(today())
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<PurgeHistoryRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setFrom(today())
    setTo(today())
    setRows(null)
    setError(null)
  }, [open])

  const search = useMutation({
    mutationFn: () => api.get<PurgeHistoryRow[]>('/dispensing/purge/history', { params: { groupId, from, to } }).then((r) => r.data),
    onSuccess: (data) => setRows(data),
    onError: (e) => setError(errorMessage(e)),
  })

  const run = () => {
    setError(null)
    if (!from) return setError('From Date is required.')
    if (!to) return setError('To Date is required.')
    if (from > to) return setError('From Date must be less than or equal to To Date.')
    search.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} title="Purge History" size="xl" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      <ErrorBanner message={error} />
      <div className="mb-4 flex flex-wrap items-end gap-2">
        <Field label="From Date">
          <input type="date" className="input" value={from} onChange={(e) => setFrom(e.target.value)} />
        </Field>
        <Field label="To Date">
          <input type="date" className="input" value={to} onChange={(e) => setTo(e.target.value)} />
        </Field>
        <button className="btn-primary" onClick={run} disabled={search.isPending}>
          {search.isPending ? <Spinner /> : <Search className="h-4 w-4" />} Search
        </button>
      </div>
      {rows === null ? (
        <EmptyState title="Select a date range" description="Choose the dates and click Search." icon={<History className="h-5 w-5" />} />
      ) : rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No history found for given dates.</p>
      ) : (
        <div className="max-h-[55vh] overflow-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="sticky top-0 border-b bg-muted">
              <tr>
                <th className="th">Group Name</th>
                <th className="th">Purge Type</th>
                <th className="th text-right">Canister No.</th>
                <th className="th">Date</th>
                <th className="th">Message</th>
                <th className="th">Result</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.key} className="border-b last:border-0">
                  <td className="td">
                    <div>{r.groupName}</div>
                    <div className="text-xs text-muted-foreground">{r.bridgeName}</div>
                  </td>
                  <td className="td">{r.purgeType || '—'}</td>
                  <td className="td text-right tabular-nums">{r.canisterNumber}</td>
                  <td className="td whitespace-nowrap">{dateTime(r.date)}</td>
                  <td className="td">{r.message}</td>
                  <td className="td">
                    <span className={clsx('badge', r.result === 'Success' ? 'bg-emerald-100 text-emerald-800' : 'bg-destructive/10 text-destructive')}>{r.result}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className="mt-3">
        <Note tone="info">Automatic purges are reported by the network bridge; manual purges come from the Purge button.</Note>
      </div>
    </Modal>
  )
}
