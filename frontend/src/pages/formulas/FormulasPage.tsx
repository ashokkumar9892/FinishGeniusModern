import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, CircleDashed, Copy, CopyPlus, DollarSign, FileText, FlaskConical, History, Pencil, Plus, Printer, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { isAdmin } from '@/lib/access'
import { money } from '@/lib/format'
import { ConfirmDialog, ErrorBanner, PageHeader, SearchInput, StatCard } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { EntityDocuments } from '@/components/EntityDocuments'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { BulkCopyModal, CopyFormulaModal, StatusBadge } from './FormulaModals'
import type { FormulaRow } from './types'

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

  useEffect(() => setSelected([]), [groupId])

  // One request per group; status + search filter instantly on the client and the KPIs always cover the whole group.
  const q = useQuery({
    queryKey: ['formulas', groupId],
    queryFn: () => api.get<FormulaRow[]>('/formulas', { params: { groupId } }).then((r) => r.data),
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
        key: 'cost',
        header: 'Cost',
        align: 'right',
        hideBelow: 'lg',
        className: 'tabular-nums',
        cell: (r) =>
          r.ingredientCount ? (
            <span title={`Formula price ${money(r.price)} · ${r.ingredientCount} ingredient${r.ingredientCount === 1 ? '' : 's'}`}>{money(r.cost)}</span>
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
            <button className="btn-icon" title="Copy" onClick={() => setCopyRow(r)}>
              <Copy className="h-4 w-4" />
            </button>
            <button className="btn-icon" title="Print formula card" onClick={() => navigate(`/formulas/${r.id}?print=1`)}>
              <Printer className="h-4 w-4" />
            </button>
            <button className="btn-icon" title="Documents" onClick={() => setDocsRow(r)}>
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
  const newButton = (
    <button className="btn-primary" onClick={() => navigate('/formulas/new')}>
      <Plus className="h-4 w-4" /> New Formulation
    </button>
  )
  const statusOptions: { key: Status; label: string; count: number }[] = [
    { key: 'all', label: 'All', count: stats.total },
    { key: 'complete', label: 'Complete', count: stats.complete },
    { key: 'incomplete', label: 'Incomplete', count: stats.incomplete },
  ]

  return (
    <>
      <PageHeader
        title="Formulas"
        breadcrumbs={['Formulas']}
        subtitle="Formulations, their ingredients, cost and colour match."
        actions={
          <>
            {admin && (
              <button className="btn-secondary" disabled={selected.length === 0} onClick={() => setBulkOpen(true)} title={selected.length ? undefined : 'Select formulas in the list first'}>
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
        toolbarRight={<SearchInput value={search} onChange={setSearch} placeholder="Search formula name, number or customer…" className="w-full sm:w-80" />}
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

      <CopyFormulaModal formula={copyRow} onClose={() => setCopyRow(null)} />
      <BulkCopyModal open={bulkOpen} ids={selected.map(Number)} onClose={() => setBulkOpen(false)} onDone={() => setSelected([])} />
      <EntityDocuments
        open={!!docsRow}
        onClose={() => setDocsRow(null)}
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
        message={`Are you sure you want to delete the "${deleteRow?.name ?? ''}" formula?`}
      />
    </>
  )
}
