import { useMemo, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, BookOpen, Copy, Eye, History, Pencil, Plus, Printer, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { ConfirmDialog, ErrorBanner, Field, Modal, Note, PageHeader, Spinner } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { SearchSelect } from '@/components/SearchSelect'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { canDeleteWorkInstruction, StatusBadge, type ApiMessage, type WiRow } from './shared'

const norm = (s: string) => s.trim().toLowerCase()

export default function WorkInstructionsPage() {
  const { groupId } = useGroup()
  const me = useMe()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()

  const [creating, setCreating] = useState(false)
  const [copyRow, setCopyRow] = useState<WiRow | null>(null)
  const [historyRow, setHistoryRow] = useState<WiRow | null>(null)
  const [deleteRow, setDeleteRow] = useState<WiRow | null>(null)

  const list = useQuery({
    queryKey: ['work-instructions', groupId],
    queryFn: () => api.get<WiRow[]>('/work-instructions', { params: { groupId } }).then((r) => r.data),
  })
  const rows = useMemo(() => list.data ?? [], [list.data])

  // Document # is not unique (legacy data has duplicates) — flag them instead of blocking.
  const duplicates = useMemo(() => {
    const counts = new Map<string, number>()
    rows.forEach((r) => counts.set(norm(r.documentNumber), (counts.get(norm(r.documentNumber)) ?? 0) + 1))
    return new Set([...counts].filter(([, n]) => n > 1).map(([k]) => k))
  }, [rows])

  const del = useMutation({
    mutationFn: (id: number) => api.delete<ApiMessage>(`/work-instructions/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleteRow(null)
      qc.invalidateQueries({ queryKey: ['work-instructions'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const canDelete = canDeleteWorkInstruction(me)
  const open = (r: WiRow, query = '') => navigate(`/work-instructions/${r.id}${query}`)

  const columns: Column<WiRow>[] = [
    {
      key: 'documentNumber',
      header: '#',
      cell: (r) => (
        <span className="inline-flex items-center gap-1.5 font-medium">
          {r.documentNumber}
          {duplicates.has(norm(r.documentNumber)) && (
            <span title="This Document # is used by more than one work instruction in this group.">
              <AlertTriangle className="h-3.5 w-3.5 text-amber-500" />
            </span>
          )}
        </span>
      ),
    },
    { key: 'groupName', header: 'Group', hideBelow: 'lg' },
    { key: 'name', header: 'Name', cell: (r) => <span className="font-medium text-foreground">{r.name}</span> },
    {
      key: 'date',
      header: 'Date/Time',
      sortValue: (r) => r.updatedAt || r.createdAt || '',
      cell: (r) => <span className="whitespace-nowrap text-muted-foreground">{dateTime(r.updatedAt) || dateTime(r.createdAt)}</span>,
      hideBelow: 'sm',
    },
    { key: 'status', header: 'Status', cell: (r) => <StatusBadge status={r.status} /> },
    { key: 'stepCount', header: 'Steps', align: 'center', hideBelow: 'md' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) => (
        <div className="flex justify-end gap-0.5">
          <button className="btn-icon" title="View" onClick={() => open(r)}>
            <Eye className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Edit" onClick={() => open(r, '?edit=1')}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Copy" onClick={() => setCopyRow(r)}>
            <Copy className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Print" onClick={() => open(r, '?print=1')}>
            <Printer className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="History" onClick={() => setHistoryRow(r)}>
            <History className="h-4 w-4" />
          </button>
          {canDelete && (
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleteRow(r)}>
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Work Instructions"
        breadcrumbs={['Work Instructions']}
        actions={
          <button className="btn-primary" onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" /> New Work Instruction
          </button>
        }
      />
      {list.isError && <ErrorBanner message={errorMessage(list.error)} />}
      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={list.isLoading}
        searchPlaceholder="Search work instructions…"
        searchText={(r) => `${r.documentNumber} ${r.name} ${r.groupName} ${r.status}`}
        initialSort={{ key: 'documentNumber', dir: 'asc' }}
        onRowClick={(r) => open(r)}
        emptyTitle="No work instructions yet"
        emptyDescription="Create a standard work instruction with illustrated steps, tools and approvals."
        emptyAction={
          <button className="btn-primary" onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" /> New Work Instruction
          </button>
        }
      />

      {creating && <CreateModal rows={rows} onClose={() => setCreating(false)} />}
      {copyRow && <CopyModal row={copyRow} onClose={() => setCopyRow(null)} />}
      <HistoryModal
        open={!!historyRow}
        onClose={() => setHistoryRow(null)}
        entityType="WorkInstruction"
        entityId={historyRow?.id ?? 0}
        title={historyRow ? `History — #${historyRow.documentNumber} ${historyRow.name}` : 'History'}
      />
      <ConfirmDialog
        open={!!deleteRow}
        message={`Are you sure you want to delete the "#${deleteRow?.documentNumber ?? ''} ${deleteRow?.name ?? ''}" work instruction?`}
        busy={del.isPending}
        onConfirm={() => deleteRow && del.mutate(deleteRow.id)}
        onClose={() => setDeleteRow(null)}
      />
    </>
  )
}

function CreateModal({ rows, onClose }: { rows: WiRow[]; onClose: () => void }) {
  const { groupId, group } = useGroup()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const [documentNumber, setDocumentNumber] = useState('')
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const duplicate = documentNumber.trim() !== '' && rows.some((r) => norm(r.documentNumber) === norm(documentNumber))

  const save = useMutation({
    mutationFn: () => api.post<ApiMessage>('/work-instructions', { groupId, documentNumber, name }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['work-instructions'] })
      navigate(`/work-instructions/${res.data.id}?edit=1`)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    setError(null)
    if (!documentNumber.trim()) return setError('Document # is required.')
    if (!name.trim()) return setError('Document Name is required.')
    save.mutate()
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Add New Work Instruction Document"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form="wi-create" disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <form id="wi-create" onSubmit={submit} className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="Group">
          <input className="input" value={group?.name ?? ''} disabled readOnly />
        </Field>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
          <Field label="Document #" required>
            <input className="input" value={documentNumber} maxLength={400} onChange={(e) => setDocumentNumber(e.target.value)} autoFocus />
          </Field>
          <Field label="Document Name" required>
            <input className="input" value={name} maxLength={400} onChange={(e) => setName(e.target.value)} />
          </Field>
        </div>
        {duplicate && <Note>Document # “{documentNumber.trim()}” is already used in this group. You can still save it.</Note>}
        <Field label="Draft Status">
          <input className="input" value="DRAFT v1" disabled readOnly />
        </Field>
      </form>
    </Modal>
  )
}

function CopyModal({ row, onClose }: { row: WiRow; onClose: () => void }) {
  const { groups } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const [newName, setNewName] = useState(`${row.name} (Copy)`)
  const [newNumber, setNewNumber] = useState(row.documentNumber)
  const [dest, setDest] = useState<number | null>(row.groupId)
  const [error, setError] = useState<string | null>(null)
  const destName = groups.find((g) => g.id === dest)?.name

  const save = useMutation({
    mutationFn: () => api.post<ApiMessage>(`/work-instructions/${row.id}/copy`, { newName, newNumber, destinationGroupId: dest }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['work-instructions'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    setError(null)
    if (!newName.trim()) return setError('New Name is required.')
    if (!dest) return setError('Please select a destination group.')
    save.mutate()
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Copy Work Instruction"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form="wi-copy" disabled={save.isPending}>
            {save.isPending && <Spinner />} <Copy className="h-4 w-4" /> Copy
          </button>
        </>
      }
    >
      <form id="wi-copy" onSubmit={submit} className="space-y-4">
        <ErrorBanner message={error} />
        <div className="flex items-center gap-2 rounded-md bg-muted/60 px-3 py-2 text-sm">
          <BookOpen className="h-4 w-4 text-muted-foreground" />
          <span className="min-w-0 truncate">
            Copying <b>#{row.documentNumber}</b> {row.name}
          </span>
        </div>
        <Field label="New Name" required>
          <input className="input" value={newName} maxLength={400} onChange={(e) => setNewName(e.target.value)} autoFocus />
        </Field>
        <Field label="New Number">
          <input className="input" value={newNumber} maxLength={400} onChange={(e) => setNewNumber(e.target.value)} />
        </Field>
        <Field label="Destination Group" required>
          <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={dest} onChange={setDest} clearable={false} />
        </Field>
        {dest !== row.groupId && destName && (
          <Note tone="info">The copy (steps, media, tools and materials) will be created in “{destName}” as DRAFT v1.</Note>
        )}
      </form>
    </Modal>
  )
}
