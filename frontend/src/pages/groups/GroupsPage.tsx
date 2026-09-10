import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, CheckSquare, Copy, Eye, History, Pencil, Plus, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { useAuth, useMe } from '@/lib/auth'
import { isAdmin, isSystemAdmin } from '@/lib/access'
import { ConfirmDialog, ErrorBanner, PageHeader, Spinner } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { GroupFormModal } from './GroupFormModal'
import { CopyGroupModal } from './CopyGroupModal'
import type { GroupRow } from './types'

export default function GroupsPage() {
  const me = useMe()
  const { refresh } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const sysAdmin = isSystemAdmin(me)

  const [editing, setEditing] = useState<GroupRow | 'new' | null>(null)
  const [viewing, setViewing] = useState<GroupRow | null>(null)
  const [copying, setCopying] = useState<GroupRow | null>(null)
  const [deleting, setDeleting] = useState<GroupRow | null>(null)
  const [history, setHistory] = useState<GroupRow | null>(null)

  const groups = useQuery({
    queryKey: ['groups'],
    queryFn: () => api.get<GroupRow[]>('/groups').then((r) => r.data),
  })

  const choose = useMutation({
    mutationFn: (g: GroupRow) => api.post(`/auth/default-group/${g.id}`),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['groups'] })
      await refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (g: GroupRow) => api.delete(`/groups/${g.id}`),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      await qc.invalidateQueries({ queryKey: ['groups'] })
      qc.invalidateQueries({ queryKey: ['lookups'] })
      await refresh()
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setDeleting(null)
    },
  })

  const columns: Column<GroupRow>[] = [
    { key: 'id', header: '#', className: 'w-14 text-muted-foreground tabular-nums' },
    {
      key: 'name',
      header: 'Name',
      cell: (g) => (
        <div className="flex items-center gap-2.5 min-w-[160px]">
          {g.logoFile ? (
            <img src={fileUrl(g.logoFile)} alt="" className="h-8 w-8 shrink-0 rounded border bg-white object-contain" loading="lazy" />
          ) : (
            <span className="h-8 w-8 shrink-0 rounded bg-muted grid place-items-center text-muted-foreground">
              <Building2 className="h-4 w-4" />
            </span>
          )}
          <div className="min-w-0">
            <div className="font-medium truncate">{g.name}</div>
            {g.checklistDeletionEnabled && <div className="text-[11px] text-muted-foreground">Checklist deletion on submit</div>}
          </div>
        </div>
      ),
    },
    { key: 'city', header: 'City', hideBelow: 'md' },
    { key: 'state', header: 'State', hideBelow: 'md' },
    { key: 'country', header: 'Country', hideBelow: 'md' },
    {
      key: 'isDefault',
      header: 'Is Default',
      sortValue: (g) => (g.isDefault ? 0 : 1),
      cell: (g) =>
        g.isDefault ? (
          <span className="badge bg-primary text-primary-foreground gap-1">
            <CheckSquare className="h-3.5 w-3.5" /> Default
          </span>
        ) : (
          <button className="btn-secondary btn-sm" onClick={() => choose.mutate(g)} disabled={choose.isPending}>
            {choose.isPending && choose.variables?.id === g.id && <Spinner className="h-3 w-3" />} Choose
          </button>
        ),
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (g) => (
        <div className="flex justify-end gap-0.5">
          {g.canEdit ? (
            <button className="btn-icon" title="Edit" onClick={() => setEditing(g)}>
              <Pencil className="h-4 w-4" />
            </button>
          ) : (
            <button className="btn-icon" title="View details" onClick={() => setViewing(g)}>
              <Eye className="h-4 w-4" />
            </button>
          )}
          {g.canCopy && (
            <button className="btn-icon" title="Copy group" onClick={() => setCopying(g)}>
              <Copy className="h-4 w-4" />
            </button>
          )}
          {isAdmin(me) && (
            <button className="btn-icon" title="History" onClick={() => setHistory(g)}>
              <History className="h-4 w-4" />
            </button>
          )}
          {g.canDelete && (
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(g)}>
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
        title="Groups"
        breadcrumbs={['Groups']}
        subtitle={sysAdmin ? 'All groups in Finish Genius' : 'Groups you belong to'}
        actions={
          sysAdmin && (
            <button className="btn-primary" onClick={() => setEditing('new')}>
              <Plus className="h-4 w-4" /> New Group
            </button>
          )
        }
      />
      {groups.isError && <ErrorBanner message={errorMessage(groups.error)} />}
      <DataTable
        rows={groups.data ?? []}
        columns={columns}
        rowKey={(g) => g.id}
        loading={groups.isLoading}
        searchPlaceholder="Search groups…"
        searchText={(g) => [g.name, g.city, g.state, g.country].filter(Boolean).join(' ')}
        initialSort={{ key: 'name', dir: 'asc' }}
        rowClassName={(g) => clsx(g.isDefault && 'bg-accent/60')}
        emptyTitle="No groups yet"
        emptyDescription={sysAdmin ? 'Create the first group to get started.' : 'You are not assigned to any group.'}
      />

      {editing && <GroupFormModal group={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {viewing && <GroupFormModal group={viewing} readOnly onClose={() => setViewing(null)} />}
      {copying && <CopyGroupModal group={copying} onClose={() => setCopying(null)} />}
      {history && (
        <HistoryModal open onClose={() => setHistory(null)} entityType="Group" entityId={history.id} title={`History — ${history.name}`} />
      )}
      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.name}" group?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}
