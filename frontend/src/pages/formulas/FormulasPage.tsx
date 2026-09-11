import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, CircleDashed, Copy, CopyPlus, DollarSign, Droplets, FileText, Filter, FlaskConical, History, Pencil, Plus, Printer, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { isAdmin } from '@/lib/access'
import { money, num } from '@/lib/format'
import { ConfirmDialog, ErrorBanner, PageHeader, SearchInput, StatCard } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { EntityDocuments } from '@/components/EntityDocuments'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { BulkCopyModal, CopyFormulaModal, CreateFormulaModal, PrintDeniedModal, StatusBadge } from './FormulaModals'
import { CleanNozzleModal, PurgeHistoryModal, PurgeModal } from './DispensingModals'
import type { DispenseSettings, FormulaRow } from './types'

type Status = 'all' | 'complete' | 'incomplete'

export default function FormulasPage() {
  const me = useMe()
  const admin = isAdmin(me)
  const { groupId } = useGroup()
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()

  const [status, setStatus] = useState<Status>('all')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<(string | number)[]>([])
  const [copyRow, setCopyRow] = useState<FormulaRow | null>(null)
  const [docsRow, setDocsRow] = useState<FormulaRow | null>(null)
  const [historyRow, setHistoryRow] = useState<FormulaRow | null>(null)
  const [deleteRow, setDeleteRow] = useState<FormulaRow | null>(null)
  const [bulkOpen, setBulkOpen] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [printDenied, setPrintDenied] = useState(false)
  const [nozzleOpen, setNozzleOpen] = useState(false)
  const [purgeOpen, setPurgeOpen] = useState(false)
  const [purgeHistoryOpen, setPurgeHistoryOpen] = useState(false)

  useEffect(() => setSelected([]), [groupId])

  // One request per group; status + search filter instantly on the client and the KPIs always cover the whole group.
  const q = useQuery({
    queryKey: ['formulas', groupId],
    queryFn: () => api.get<FormulaRow[]>('/formulas', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
  const settings = useQuery({
    queryKey: ['dispense-settings', groupId],
    queryFn: () => api.get<DispenseSettings>('/dispensing/settings', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
  const rows = useMemo(() => q.data ?? [], [q.data])

  const filtered = useMemo(() => {
    const s = search.trim().toLowerCase()
    return rows.filter(
      (r) =>
        (status === 'all' || r.isComplete === (status === 'complete')) &&
        (!s || `${r.name} ${r.number ?? ''} ${r.customerName ?? ''}`.toLowerCase().includes(s)),
    )
  }, [rows, status, search])

  const stats = useMemo(() => {
    const complete = rows.filter((r) => r.isComplete).length
    const costed = rows.filter((r) => r.ingredientCount > 0)
    return {
      total: rows.length,
      complete,
      incomplete: rows.length - complete,
      avgCost: costed.length ? costed.reduce((s, r) => s + r.cost, 0) / costed.length : 0,
      costedCount: costed.length,
    }
  }, [rows])

  const remove = useMutation({
    mutationFn: (id: number) => api.delete<{ message: string }>(`/formulas/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleteRow(null)
      setSelected((s) => s.filter((k) => k !== deleteRow?.id))
      qc.invalidateQueries({ queryKey: ['formulas'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const columns = useMemo<Column<FormulaRow>[]>(
    () => [
      { key: 'id', header: '#', className: 'w-14 tabular-nums' },
      { key: 'groupName', header: 'Group', hideBelow: 'xl' },
      { key: 'categoryName', header: 'Category', hideBelow: 'md', cell: (r) => r.categoryName ?? <span className="text-muted-foreground">—</span> },
      {
        key: 'name',
        header: 'Formula Name',
        cell: (r) => (
          <Link to={`/formulas/${r.id}`} className="font-medium text-primary hover:underline">
            {r.name}
          </Link>
        ),
      },
      { key: 'number', header: 'Formula Number' },
      { key: 'customerName', header: 'Customer Name', hideBelow: 'sm' },
      { key: 'status', header: 'Status', sortValue: (r) => (r.isComplete ? 1 : 0), cell: (r) => <StatusBadge complete={r.isComplete} /> },
      {
        key: 'gramsInBatch',
        header: 'Grams in Batch',
        align: 'right',
        hideBelow: 'lg',
        className: 'tabular-nums',
        cell: (r) => (r.gramsInBatch ? num(r.gramsInBatch, 2) : <span className="text-muted-foreground">—</span>),
      },
      {
        key: 'cost',
        header: 'Formula Cost',
        align: 'right',
        hideBelow: 'lg',
        className: 'tabular-nums',
        cell: (r) =>
          r.ingredientCount ? (
            <span title={`Material cost ${money(r.cost)} · formula price ${money(r.price)} · ${r.ingredientCount} ingredient${r.ingredientCount === 1 ? '' : 's'}`}>{money(r.price)}</span>
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        key: 'actions',
        header: 'Actions',
        sortable: false,
        align: 'right',
        cell: (r) => (
          <div className="flex items-center justify-end gap-0.5">
            <button className="btn-icon" title="Edit" onClick={() => navigate(`/formulas/${r.id}`)}>
              <Pencil className="h-4 w-4" />
            </button>
            <button className="btn-icon" title="Copy to New" onClick={() => setCopyRow(r)}>
              <Copy className="h-4 w-4" />
            </button>
            <button
              className={clsx('btn-icon', !r.isComplete && 'opacity-50')}
              title={r.isComplete ? 'Print' : 'Print (incomplete formulas cannot be printed)'}
              onClick={() => (r.isComplete ? navigate(`/formulas/${r.id}?print=1`) : setPrintDenied(true))}
            >
              <Printer className="h-4 w-4" />
            </button>
            <button
              className={clsx('btn-icon', r.hasDocs && 'bg-primary/10 text-primary hover:bg-primary/20')}
              title={r.hasDocs ? 'Documents (has documents)' : 'Documents'}
              onClick={() => setDocsRow(r)}
            >
              <FileText className="h-4 w-4" />
            </button>
            {admin && (
              <>
                <button className="btn-icon" title="History" onClick={() => setHistoryRow(r)}>
                  <History className="h-4 w-4" />
                </button>
                <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleteRow(r)}>
                  <Trash2 className="h-4 w-4" />
                </button>
              </>
            )}
          </div>
        ),
      },
    ],
    [admin, navigate],
  )

  const filtering = status !== 'all' || search.trim() !== ''
  const bulkIds = selected.length > 0 ? selected.map(Number) : filtered.map((r) => r.id)
  const newButton = (
    <button className="btn-primary" onClick={() => setCreateOpen(true)} disabled={groupId <= 0}>
      <Plus className="h-4 w-4" /> New Formulation
    </button>
  )
  const statusOptions: { key: Status; label: string; count: number }[] = [
    { key: 'all', label: 'All', count: stats.total },
    { key: 'complete', label: 'Complete', count: stats.complete },
    { key: 'incomplete', label: 'Incomplete', count: stats.incomplete },
  ]
  const canPurge = !!settings.data?.hasDispensers && !!settings.data?.hasBridges

  return (
    <>
      <PageHeader
        title="Formulas"
        breadcrumbs={['Formulas']}
        subtitle="Formulations, their ingredients, cost and colour match."
        actions={
          <>
            {canPurge && (
              <button className="btn-secondary" onClick={() => setPurgeOpen(true)}>
                <Trash2 className="h-4 w-4" /> Purge
              </button>
            )}
            <button className="btn-secondary" onClick={() => setNozzleOpen(true)} disabled={groupId <= 0}>
              <Filter className="h-4 w-4" /> Clean Nozzle
            </button>
            {admin && (
              <button className="btn-secondary" onClick={() => setPurgeHistoryOpen(true)} disabled={groupId <= 0}>
                <History className="h-4 w-4" /> Purge History
              </button>
            )}
            {admin && (
              <button
                className="btn-secondary"
                disabled={bulkIds.length === 0}
                onClick={() => setBulkOpen(true)}
                title={selected.length ? `Copy the ${selected.length} selected formulas` : 'No rows selected: copies every formula in the (filtered) list'}
              >
                <CopyPlus className="h-4 w-4" /> Bulk Copy{selected.length > 0 && ` (${selected.length})`}
              </button>
            )}
            {newButton}
          </>
        }
      />

      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard label="Total formulas" value={stats.total} icon={<FlaskConical className="h-4 w-4" />} hint={q.data ? 'In the selected group' : undefined} />
        <StatCard label="Complete" value={stats.complete} tone="success" icon={<CheckCircle2 className="h-4 w-4" />} hint={stats.total ? `${Math.round((stats.complete / stats.total) * 100)}% of formulas` : undefined} />
        <StatCard label="Incomplete" value={stats.incomplete} icon={<CircleDashed className="h-4 w-4" />} hint="Still being developed" />
        <StatCard label="Avg material cost" value={money(stats.avgCost)} tone="primary" icon={<DollarSign className="h-4 w-4" />} hint={stats.costedCount ? `Across ${stats.costedCount} formulas with ingredients` : 'No ingredients yet'} />
      </div>

      {settings.data?.cleaningRequired && (
        <div className="mb-4 flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
          <Droplets className="mt-0.5 h-4 w-4 shrink-0" />
          <span>The nozzle cleaning interval has passed since the last dispense. Users are asked to confirm the nozzle cleaning before the next dispense.</span>
        </div>
      )}

      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}

      <DataTable
        rows={filtered}
        columns={columns}
        rowKey={(r) => r.id}
        loading={q.isLoading}
        initialSort={{ key: 'id', dir: 'desc' }}
        selectable={admin}
        selected={selected}
        onSelectedChange={setSelected}
        toolbar={
          <div className="inline-flex rounded-md border bg-muted/40 p-0.5" role="group" aria-label="Status">
            {statusOptions.map((o) => (
              <button
                key={o.key}
                type="button"
                aria-pressed={status === o.key}
                onClick={() => setStatus(o.key)}
                className={clsx(
                  'h-8 rounded px-3 text-xs font-medium transition-colors',
                  status === o.key ? 'bg-card text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {o.label} <span className="ml-0.5 tabular-nums opacity-70">{o.count}</span>
              </button>
            ))}
          </div>
        }
        toolbarRight={<SearchInput value={search} onChange={setSearch} placeholder="Search by Formula Name, Formula Number or Customer Name" className="w-full sm:w-96" />}
        emptyTitle={filtering ? 'No formulas match these filters' : 'No formulas yet'}
        emptyDescription={filtering ? 'Try another status or search term.' : 'Create your first formulation to track ingredients, cost and VOC content.'}
        emptyAction={
          filtering ? (
            <button className="btn-secondary" onClick={() => { setStatus('all'); setSearch('') }}>
              Clear filters
            </button>
          ) : (
            newButton
          )
        }
      />

      <CreateFormulaModal open={createOpen} onClose={() => setCreateOpen(false)} />
      <CopyFormulaModal formula={copyRow} onClose={() => setCopyRow(null)} />
      <BulkCopyModal open={bulkOpen} ids={bulkIds} onClose={() => setBulkOpen(false)} onDone={() => setSelected([])} />
      <PrintDeniedModal open={printDenied} onClose={() => setPrintDenied(false)} />
      <CleanNozzleModal open={nozzleOpen} onClose={() => setNozzleOpen(false)} groupId={groupId} />
      <PurgeModal open={purgeOpen} onClose={() => setPurgeOpen(false)} groupId={groupId} />
      <PurgeHistoryModal open={purgeHistoryOpen} onClose={() => setPurgeHistoryOpen(false)} groupId={groupId} />
      <EntityDocuments
        open={!!docsRow}
        onClose={() => {
          setDocsRow(null)
          qc.invalidateQueries({ queryKey: ['formulas'] })
        }}
        entityType="Formula"
        entityId={docsRow?.id ?? 0}
        groupId={docsRow?.groupId ?? groupId}
        title={docsRow?.name}
      />
      <HistoryModal open={!!historyRow} onClose={() => setHistoryRow(null)} entityType="Formula" entityId={historyRow?.id ?? 0} title={historyRow ? `History — ${historyRow.name}` : undefined} />
      <ConfirmDialog
        open={!!deleteRow}
        onClose={() => setDeleteRow(null)}
        busy={remove.isPending}
        onConfirm={() => deleteRow && remove.mutate(deleteRow.id)}
        message={`Are you sure you want to delete the Formula "${deleteRow?.number ? `${deleteRow.number} - ` : ''}${deleteRow?.name ?? ''}"?`}
      />
    </>
  )
}
