import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, ArchiveRestore, Copy, CopyPlus, FileText, History, Pencil, Plus, Printer, Tag } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { isAdmin } from '@/lib/access'
import { date } from '@/lib/format'
import { useToast } from '@/components/toast'
import { DataTable, type Column } from '@/components/DataTable'
import { Checkbox, ConfirmDialog, ErrorBanner, Field, Modal, Note, PageHeader, SearchInput, Spinner } from '@/components/ui'
import { EntityDocuments } from '@/components/EntityDocuments'
import { HistoryModal } from '@/components/HistoryModal'
import { BulkCopyModal, PrintPortal } from './shared'
import { SchedulePrintModal } from './SchedulePrint'
import type { ScheduleRow } from './types'

interface LabelPrinter {
  id: number
  name: string
  groupName?: string | null
}

const printerKey = (groupId: number) => `fg.labelPrinter.${groupId}`

function readStorage(key: string) {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function writeStorage(key: string, value: string | null) {
  try {
    if (value == null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    /* storage unavailable */
  }
}

export default function ProcessSchedulesPage() {
  const { groupId, group } = useGroup()
  const me = useMe()
  const admin = isAdmin(me)
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()

  const [search, setSearch] = useState('')
  const [showArchived, setShowArchived] = useState(false)
  const [selected, setSelected] = useState<(string | number)[]>([])
  const [bulkOpen, setBulkOpen] = useState(false)
  const [copying, setCopying] = useState<{ row: ScheduleRow; master: boolean } | null>(null)
  const [printing, setPrinting] = useState<ScheduleRow | null>(null)
  const [labelFor, setLabelFor] = useState<ScheduleRow | null>(null)
  const [history, setHistory] = useState<ScheduleRow | null>(null)
  const [docs, setDocs] = useState<ScheduleRow | null>(null)
  const [archiving, setArchiving] = useState<ScheduleRow | null>(null)
  const [printerId, setPrinterId] = useState<number | null>(null)

  useEffect(() => {
    setSelected([])
    const saved = Number(readStorage(printerKey(groupId)) ?? 0)
    setPrinterId(saved > 0 ? saved : null)
  }, [groupId])

  const q = useQuery({
    queryKey: ['process-schedules', groupId, showArchived],
    queryFn: () => api.get<ScheduleRow[]>('/process-schedules', { params: { groupId, includeArchived: showArchived } }).then((r) => r.data),
  })
  const rows = q.data ?? []

  // Label printers come from the Devices module; a missing endpoint or no printers simply shows an empty list.
  const printers = useQuery({
    queryKey: ['devices', groupId, 5],
    queryFn: async () => {
      try {
        const r = await api.get<LabelPrinter[] | { items: LabelPrinter[] }>('/devices', { params: { groupId, type: 5 } })
        return Array.isArray(r.data) ? r.data : (r.data?.items ?? [])
      } catch {
        return [] as LabelPrinter[]
      }
    },
    retry: false,
    staleTime: 5 * 60_000,
  })
  const printerOptions = useMemo(
    () => (printers.data ?? []).map((p) => ({ id: p.id, label: `${p.name} [${p.groupName ?? group?.name ?? ''}]` })),
    [printers.data, group],
  )
  const printer = printerOptions.find((p) => p.id === printerId)

  const choosePrinter = (id: number | null) => {
    setPrinterId(id)
    writeStorage(printerKey(groupId), id ? String(id) : null)
  }

  const archive = useMutation({
    mutationFn: (id: number) => api.post(`/process-schedules/${id}/archive`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setArchiving(null)
      qc.invalidateQueries({ queryKey: ['process-schedules'] })
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setArchiving(null)
    },
  })
  const restore = useMutation({
    mutationFn: (id: number) => api.post(`/process-schedules/${id}/restore`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['process-schedules'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const columns: Column<ScheduleRow>[] = [
    { key: 'id', header: '#', className: 'w-16 text-muted-foreground' },
    { key: 'groupName', header: 'Group', hideBelow: 'lg' },
    {
      key: 'name',
      header: 'Name',
      cell: (r) => (
        <span className="inline-flex items-center gap-2">
          {r.isArchived ? <span className="font-medium text-muted-foreground">{r.name}</span> : <Link to={`/process-schedules/${r.id}`} className="font-medium hover:text-primary">{r.name}</Link>}
          {r.isArchived && <span className="badge bg-muted text-muted-foreground">Archived</span>}
        </span>
      ),
    },
    { key: 'number', header: 'Number' },
    { key: 'customerName', header: 'Customer Name', hideBelow: 'sm' },
    { key: 'stepCount', header: 'Steps', align: 'center', hideBelow: 'md' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) => (
        <div className="flex justify-end gap-0.5 whitespace-nowrap">
          {!r.isArchived && (
            <>
              <button className="btn-icon" title="Edit" onClick={() => navigate(`/process-schedules/${r.id}`)}><Pencil className="h-4 w-4" /></button>
              <button className="btn-icon" title="Clone (without step edits)" onClick={() => setCopying({ row: r, master: false })}><Copy className="h-4 w-4" /></button>
              <button className="btn-icon" title="Copy Master (with step edits)" onClick={() => setCopying({ row: r, master: true })}><CopyPlus className="h-4 w-4" /></button>
            </>
          )}
          <button className="btn-icon" title="Print" onClick={() => setPrinting(r)}><Printer className="h-4 w-4" /></button>
          <button className="btn-icon" title="History" onClick={() => setHistory(r)}><History className="h-4 w-4" /></button>
          {!r.isArchived && (
            <>
              <button className="btn-icon" title="Documents" onClick={() => setDocs(r)}><FileText className="h-4 w-4" /></button>
              <button className="btn-icon" title="Print Sample Label" onClick={() => setLabelFor(r)}><Tag className="h-4 w-4" /></button>
            </>
          )}
          {admin &&
            (r.isArchived ? (
              <button className="btn-icon hover:text-success" title="Restore" disabled={restore.isPending} onClick={() => restore.mutate(r.id)}><ArchiveRestore className="h-4 w-4" /></button>
            ) : (
              <button className="btn-icon hover:text-destructive" title="Archive" onClick={() => setArchiving(r)}><Archive className="h-4 w-4" /></button>
            ))}
        </div>
      ),
    },
  ]

  const bulkCopy = async (destinationGroupId: number) => {
    const res = await api.post('/process-schedules/bulk-copy', { ids: selected, destinationGroupId })
    qc.invalidateQueries({ queryKey: ['process-schedules'] })
    qc.invalidateQueries({ queryKey: ['process-steps'] })
    setSelected([])
    return res.data.message as string
  }

  return (
    <>
      <PageHeader
        title="Process Schedule List"
        breadcrumbs={['Processes', 'Process Schedule List']}
        actions={
          <>
            <button className="btn-secondary" disabled={selected.length === 0} onClick={() => setBulkOpen(true)} title={selected.length ? undefined : 'Select schedules first'}>
              <Copy className="h-4 w-4" /> Bulk Copy{selected.length > 0 && ` (${selected.length})`}
            </button>
            <button className="btn-primary" onClick={() => navigate('/process-schedules/new')}>
              <Plus className="h-4 w-4" /> Create New
            </button>
          </>
        }
      />

      <div className="card p-4 mb-4 grid gap-4 md:grid-cols-[1fr_20rem] items-end">
        <Field label="Search">
          <SearchInput value={search} onChange={setSearch} placeholder="Search by schedule name, number or customer…" />
        </Field>
        <Field label="Label Printer" hint={printers.isFetched && printerOptions.length === 0 ? 'No label printers set up for this group (Dashboard › Devices).' : undefined}>
          <select className="input" value={printerId ?? ''} onChange={(e) => choosePrinter(e.target.value ? Number(e.target.value) : null)} disabled={printerOptions.length === 0}>
            <option value="">{printers.isLoading ? 'Loading…' : 'Select a label printer'}</option>
            {printerOptions.map((p) => (
              <option key={p.id} value={p.id}>{p.label}</option>
            ))}
          </select>
        </Field>
      </div>

      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}
      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={q.isLoading}
        search={search}
        searchText={(r) => `${r.name} ${r.number} ${r.customerName ?? ''}`}
        initialSort={{ key: 'id', dir: 'desc' }}
        selectable
        selected={selected}
        onSelectedChange={setSelected}
        rowClassName={(r) => (r.isArchived ? 'opacity-70' : undefined)}
        toolbar={admin ? <Checkbox checked={showArchived} onChange={setShowArchived} label="Show archived" /> : undefined}
        emptyTitle="No process schedules yet"
        emptyDescription="A process schedule strings process steps together into a complete finishing system."
        emptyAction={<button className="btn-primary btn-sm" onClick={() => navigate('/process-schedules/new')}><Plus className="h-4 w-4" /> Create New</button>}
      />

      <CopyScheduleModal target={copying} onClose={() => setCopying(null)} />
      <BulkCopyModal open={bulkOpen} onClose={() => setBulkOpen(false)} count={selected.length} onCopy={bulkCopy} />
      <SchedulePrintModal open={!!printing} scheduleId={printing?.id ?? null} onClose={() => setPrinting(null)} />
      <SampleLabelModal schedule={labelFor} printer={printer?.label} onClose={() => setLabelFor(null)} />
      <HistoryModal open={!!history} onClose={() => setHistory(null)} entityType="ProcessSchedule" entityId={history?.id ?? 0} title={`History — ${history?.name ?? ''}`} />
      {docs && <EntityDocuments open onClose={() => setDocs(null)} entityType="ProcessSchedule" entityId={docs.id} groupId={docs.groupId} title={`Documents — ${docs.name}`} />}
      <ConfirmDialog
        open={!!archiving}
        title="Archive Confirmation"
        confirmLabel="Archive"
        message={`Are you sure you want to archive the "${archiving?.name}" process schedule? It can be restored later from "Show archived".`}
        busy={archive.isPending}
        onConfirm={() => archiving && archive.mutate(archiving.id)}
        onClose={() => setArchiving(null)}
      />
    </>
  )
}

function CopyScheduleModal({ target, onClose }: { target: { row: ScheduleRow; master: boolean } | null; onClose: () => void }) {
  const toast = useToast()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [number, setNumber] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (target) {
      setName(target.row.name)
      setNumber(target.row.number)
      setError(null)
    }
  }, [target])

  const copy = useMutation({
    mutationFn: () =>
      api.post(`/process-schedules/${target!.row.id}/${target!.master ? 'copy-master' : 'clone'}`, { newName: name.trim(), newNumber: number.trim() }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['process-schedules'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    if (!name.trim() || !number.trim()) return setError('New Name and New Number are required.')
    if (number.trim() === target?.row.number) return setError('Schedule # already exists in this group. Enter a different New Number.')
    setError(null)
    copy.mutate()
  }

  const master = target?.master
  return (
    <Modal
      open={!!target}
      onClose={onClose}
      title={master ? `Copy Process Schedule ${target?.row.name ?? ''}` : `Duplicate Process Schedule ${target?.row.name ?? ''}`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={copy.isPending}>
            {copy.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <div className="mb-4">
        <Note tone={master ? 'info' : 'warning'}>
          {master
            ? 'Note: A copy of this schedule will include all edits made to associated steps.'
            : 'Note: A duplicate of this schedule will NOT include all edits made to associated steps.'}
        </Note>
      </div>
      <ErrorBanner message={error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="New Name" required>
          <input className="input" value={name} maxLength={400} autoFocus onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="New Number" required hint="Must be unique in the group.">
          <input className="input" value={number} maxLength={400} onChange={(e) => setNumber(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}

function LabelBody({ schedule, printer }: { schedule: ScheduleRow; printer?: string }) {
  return (
    <div className="w-[4in] h-[2in] box-border border border-slate-400 bg-white text-slate-900 p-[0.14in] flex flex-col justify-between overflow-hidden" style={{ fontFamily: 'Inter, Arial, sans-serif' }}>
      <div>
        <div className="flex items-center justify-between text-[8pt] uppercase tracking-[0.18em] text-slate-500">
          <span>Process Schedule</span>
          <span>{date(new Date().toISOString())}</span>
        </div>
        <div className="mt-[2pt] text-[15pt] font-bold leading-tight truncate">{schedule.name}</div>
        <div className="text-[22pt] font-extrabold leading-none tracking-tight">#{schedule.number}</div>
      </div>
      <div className="text-[8.5pt] leading-tight">
        {schedule.customerName && <div className="truncate">Customer: <b>{schedule.customerName}</b></div>}
        <div className="flex justify-between gap-2 text-slate-600">
          <span className="truncate">{schedule.groupName}</span>
          {printer && <span className="truncate text-right">{printer}</span>}
        </div>
      </div>
    </div>
  )
}

function SampleLabelModal({ schedule, printer, onClose }: { schedule: ScheduleRow | null; printer?: string; onClose: () => void }) {
  return (
    <>
      <Modal
        open={!!schedule}
        onClose={onClose}
        title="Print Sample Label"
        footer={
          <>
            <button className="btn-secondary" onClick={onClose}>Close</button>
            <button className="btn-primary" onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print Label
            </button>
          </>
        }
      >
        {schedule && (
          <div className="space-y-3">
            <div className="rounded-md bg-muted p-4 overflow-x-auto flex justify-center">
              <div className="shadow-lg">
                <LabelBody schedule={schedule} printer={printer} />
              </div>
            </div>
            <p className="text-xs text-muted-foreground">
              4 × 2 in label. {printer ? <>Selected printer: <b>{printer}</b>. Choose it in the print dialog.</> : 'Select a label printer on the list page to show it on the label.'}
            </p>
          </div>
        )}
      </Modal>
      {schedule && (
        <PrintPortal page="4in 2in">
          <LabelBody schedule={schedule} printer={printer} />
        </PrintPortal>
      )}
    </>
  )
}
