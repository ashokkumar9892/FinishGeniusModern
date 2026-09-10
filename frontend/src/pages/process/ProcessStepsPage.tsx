import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Eye, FileText, History, Pencil, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useLookups, useMe } from '@/lib/auth'
import { isAdmin } from '@/lib/access'
import { dateTime } from '@/lib/format'
import { useToast } from '@/components/toast'
import { DataTable, type Column } from '@/components/DataTable'
import { ConfirmDialog, ErrorBanner, Field, Modal, PageHeader, SearchInput, Spinner } from '@/components/ui'
import { EntityDocuments } from '@/components/EntityDocuments'
import { HistoryModal } from '@/components/HistoryModal'
import { BulkCopyModal, useDebounced, ViewStepModal } from './shared'
import type { ProcessStepRow } from './types'

const SECTOR_KEY = 'fg.processSteps.sector'

export default function ProcessStepsPage() {
  const { groupId } = useGroup()
  const me = useMe()
  const admin = isAdmin(me)
  const navigate = useNavigate()
  const lookups = useLookups()
  const toast = useToast()
  const qc = useQueryClient()

  const [sectorId, setSectorId] = useState<number>(() => Number(localStorage.getItem(SECTOR_KEY) ?? 0))
  const [search, setSearch] = useState('')
  const debounced = useDebounced(search.trim(), 300)
  const [selected, setSelected] = useState<(string | number)[]>([])
  const [bulkOpen, setBulkOpen] = useState(false)
  const [copying, setCopying] = useState<ProcessStepRow | null>(null)
  const [deleting, setDeleting] = useState<ProcessStepRow | null>(null)
  const [viewing, setViewing] = useState<ProcessStepRow | null>(null)
  const [docs, setDocs] = useState<ProcessStepRow | null>(null)
  const [history, setHistory] = useState<ProcessStepRow | null>(null)

  useEffect(() => {
    try {
      localStorage.setItem(SECTOR_KEY, String(sectorId))
    } catch {
      /* storage unavailable */
    }
  }, [sectorId])
  useEffect(() => setSelected([]), [groupId])

  const q = useQuery({
    queryKey: ['process-steps', groupId, sectorId, debounced],
    queryFn: () =>
      api
        .get<ProcessStepRow[]>('/process-steps', { params: { groupId, industrySectorId: sectorId || undefined, search: debounced || undefined } })
        .then((r) => r.data),
    placeholderData: keepPreviousData,
  })
  const rows = q.data ?? []

  const del = useMutation({
    mutationFn: (id: number) => api.delete(`/process-steps/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['process-steps'] })
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setDeleting(null)
    },
  })

  const columns: Column<ProcessStepRow>[] = [
    { key: 'id', header: '#', className: 'w-16 text-muted-foreground' },
    { key: 'groupName', header: 'Group', hideBelow: 'lg' },
    {
      key: 'name',
      header: 'Process Step Name',
      cell: (r) => (
        <div className="min-w-0">
          <Link to={`/process-steps/${r.id}`} className="font-medium hover:text-primary">{r.name}</Link>
          <div className="text-xs text-muted-foreground">
            {r.filledCount} sub step{r.filledCount === 1 ? '' : 's'} filled
            {r.scheduleCount > 0 && ` · used in ${r.scheduleCount} schedule${r.scheduleCount === 1 ? '' : 's'}`}
          </div>
        </div>
      ),
    },
    { key: 'industrySectorName', header: 'Industry Sector', hideBelow: 'sm' },
    {
      key: 'updatedAt',
      header: 'Last Updated',
      hideBelow: 'xl',
      sortValue: (r) => r.updatedAt ?? r.createdAt,
      cell: (r) => <span className="text-muted-foreground">{dateTime(r.updatedAt ?? r.createdAt)}</span>,
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) => (
        <div className="flex justify-end gap-0.5 whitespace-nowrap">
          <button className="btn-icon" title="Edit" onClick={() => navigate(`/process-steps/${r.id}`)}><Pencil className="h-4 w-4" /></button>
          <button className="btn-icon" title="View Step" onClick={() => setViewing(r)}><Eye className="h-4 w-4" /></button>
          <button className="btn-icon" title="Copy" onClick={() => setCopying(r)}><Copy className="h-4 w-4" /></button>
          <button className="btn-icon" title="Documents" onClick={() => setDocs(r)}><FileText className="h-4 w-4" /></button>
          <button className="btn-icon" title="History" onClick={() => setHistory(r)}><History className="h-4 w-4" /></button>
          {admin && (
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(r)}><Trash2 className="h-4 w-4" /></button>
          )}
        </div>
      ),
    },
  ]

  const bulkCopy = async (destinationGroupId: number) => {
    const res = await api.post('/process-steps/bulk-copy', { ids: selected, destinationGroupId })
    qc.invalidateQueries({ queryKey: ['process-steps'] })
    setSelected([])
    return res.data.message as string
  }

  return (
    <>
      <PageHeader
        title="Process Step List"
        breadcrumbs={['Processes', 'Process Step List']}
        actions={
          <>
            <button className="btn-secondary" disabled={selected.length === 0} onClick={() => setBulkOpen(true)} title={selected.length ? undefined : 'Select steps first'}>
              <Copy className="h-4 w-4" /> Bulk Copy{selected.length > 0 && ` (${selected.length})`}
            </button>
            <button className="btn-primary" onClick={() => navigate(`/process-steps/new${sectorId ? `?sector=${sectorId}` : ''}`)}>
              <Plus className="h-4 w-4" /> Create New
            </button>
          </>
        }
      />

      <div className="card p-4 mb-4 grid gap-4 sm:grid-cols-[16rem_1fr] items-end">
        <Field label="Industry Sector">
          <select className="input" value={sectorId} onChange={(e) => setSectorId(Number(e.target.value))}>
            <option value={0}>All industry sectors</option>
            {(lookups.data?.industrySectors ?? []).map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </Field>
        <Field label="Search">
          <SearchInput value={search} onChange={setSearch} placeholder="Search process steps…" />
        </Field>
      </div>

      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}
      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={q.isLoading}
        initialSort={{ key: 'id', dir: 'desc' }}
        selectable
        selected={selected}
        onSelectedChange={setSelected}
        emptyTitle={debounced || sectorId ? 'No process steps match these filters' : 'No process steps yet'}
        emptyDescription={debounced || sectorId ? undefined : 'Create a process step to describe how one operation is performed.'}
        emptyAction={
          <button className="btn-primary btn-sm" onClick={() => navigate('/process-steps/new')}>
            <Plus className="h-4 w-4" /> Create New
          </button>
        }
      />

      <CopyStepModal step={copying} onClose={() => setCopying(null)} />
      <BulkCopyModal open={bulkOpen} onClose={() => setBulkOpen(false)} count={selected.length} onCopy={bulkCopy} />
      <ViewStepModal open={!!viewing} onClose={() => setViewing(null)} stepId={viewing?.id ?? null} title={viewing?.name} />
      {docs && (
        <EntityDocuments open onClose={() => setDocs(null)} entityType="ProcessStep" entityId={docs.id} groupId={docs.groupId} title={`Documents — ${docs.name}`} />
      )}
      <HistoryModal open={!!history} onClose={() => setHistory(null)} entityType="ProcessStep" entityId={history?.id ?? 0} title={`History — ${history?.name ?? ''}`} />
      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.name}" process step?`}
        busy={del.isPending}
        onConfirm={() => deleting && del.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

function CopyStepModal({ step, onClose }: { step: ProcessStepRow | null; onClose: () => void }) {
  const toast = useToast()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (step) {
      setName(`${step.name} (Copy)`)
      setError(null)
    }
  }, [step])

  const copy = useMutation({
    mutationFn: () => api.post(`/process-steps/${step!.id}/copy`, { newName: name.trim() }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['process-steps'] })
      onClose()
      if (res.data.id) navigate(`/process-steps/${res.data.id}`)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  return (
    <Modal
      open={!!step}
      onClose={onClose}
      size="sm"
      title={`Copy Process Step ${step?.name ?? ''}`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>Cancel</button>
          <button className="btn-primary" disabled={copy.isPending || !name.trim()} onClick={() => copy.mutate()}>
            {copy.isPending ? <Spinner /> : <Copy className="h-4 w-4" />} Copy
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <Field label="New Name" required>
        <input className="input" value={name} maxLength={400} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && name.trim() && copy.mutate()} />
      </Field>
    </Modal>
  )
}
