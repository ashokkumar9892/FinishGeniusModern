import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Download, History, KeyRound, Pencil, Plus, Power, PowerOff, Trash2, XCircle } from 'lucide-react'
import clsx from 'clsx'
import { api, download, errorMessage } from '@/lib/api'
import { useGroup, useLookups, useMe } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { ConfirmDialog, ErrorBanner, PageHeader } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { SearchSelect } from '@/components/SearchSelect'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { UserFormModal } from './UserFormModal'
import { ResetPasswordModal } from './ResetPasswordModal'
import { ROLE_LABELS, type UserRow } from './types'

const ROLE_STYLE: Record<string, string> = {
  SystemAdmin: 'bg-violet-100 text-violet-800',
  SupportAgent: 'bg-sky-100 text-sky-800',
  GroupAdmin: 'bg-amber-100 text-amber-800',
  FGProPlus: 'bg-emerald-100 text-emerald-800',
  FGPro: 'bg-slate-100 text-slate-700',
}

export default function UsersPage() {
  const me = useMe()
  const { groupId, groups } = useGroup()
  const lookups = useLookups()
  const qc = useQueryClient()
  const toast = useToast()

  const [filterGroup, setFilterGroup] = useState<number | null>(groupId)
  useEffect(() => setFilterGroup(groupId), [groupId])

  const [editing, setEditing] = useState<UserRow | 'new' | null>(null)
  const [resetting, setResetting] = useState<UserRow | null>(null)
  const [toggling, setToggling] = useState<UserRow | null>(null)
  const [deleting, setDeleting] = useState<UserRow | null>(null)
  const [history, setHistory] = useState<UserRow | null>(null)

  const roleLabel = useMemo(() => {
    const map = { ...ROLE_LABELS }
    lookups.data?.roles.forEach((r) => (map[r.value] = r.label))
    return map
  }, [lookups.data])

  const users = useQuery({
    queryKey: ['users', filterGroup],
    queryFn: () => api.get<UserRow[]>('/users', { params: { groupId: filterGroup ?? undefined } }).then((r) => r.data),
  })

  const onDone = async (message: string) => {
    toast.success(message)
    await qc.invalidateQueries({ queryKey: ['users'] })
  }

  const toggle = useMutation({
    mutationFn: (u: UserRow) => api.post(`/users/${u.id}/status`, { disabled: !u.disabled }),
    onSuccess: async (res) => {
      setToggling(null)
      await onDone(res.data.message)
    },
    onError: (e) => {
      setToggling(null)
      toast.error(errorMessage(e))
    },
  })

  const remove = useMutation({
    mutationFn: (u: UserRow) => api.delete(`/users/${u.id}`),
    onSuccess: async (res) => {
      setDeleting(null)
      await onDone(res.data.message)
    },
    onError: (e) => {
      setDeleting(null)
      toast.error(errorMessage(e))
    },
  })

  const downloadCertificate = async (u: UserRow) => {
    try {
      await download(`/users/${u.id}/agreement-certificate`, `agreement-acceptance-${u.username}.html`)
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  const columns: Column<UserRow>[] = [
    { key: 'id', header: '#', className: 'w-14 text-muted-foreground tabular-nums' },
    { key: 'groupName', header: 'Group', cell: (u) => <span className="whitespace-nowrap">{u.groupName}</span> },
    { key: 'email', header: 'Email', cell: (u) => <span className="break-all">{u.email}</span> },
    {
      key: 'username',
      header: 'Username',
      cell: (u) => (
        <span className="font-medium">
          {u.username}
          {u.isSelf && <span className="ml-1.5 badge bg-muted text-muted-foreground">you</span>}
        </span>
      ),
    },
    { key: 'firstName', header: 'First Name', hideBelow: 'sm' },
    { key: 'lastName', header: 'Last Name', hideBelow: 'sm' },
    {
      key: 'agreementAccepted',
      header: 'User Agreement Status',
      align: 'center',
      sortValue: (u) => (u.agreementAccepted ? 1 : 0),
      cell: (u) =>
        u.agreementAccepted ? (
          <span title={u.agreementAcceptedAt ? `Accepted ${dateTime(u.agreementAcceptedAt)}` : 'Accepted'} className="inline-flex">
            <CheckCircle2 className="h-5 w-5 text-success" aria-label="Accepted" />
          </span>
        ) : (
          <span title="Not accepted yet" className="inline-flex">
            <XCircle className="h-5 w-5 text-destructive" aria-label="Not accepted" />
          </span>
        ),
    },
    {
      key: 'disabled',
      header: 'Status',
      sortValue: (u) => (u.disabled ? 'Disabled' : 'Enabled'),
      cell: (u) => (
        <span className={clsx('badge', u.disabled ? 'bg-destructive/10 text-destructive' : 'bg-success/10 text-success')}>
          {u.disabled ? 'Disabled' : 'Enabled'}
        </span>
      ),
    },
    {
      key: 'roles',
      header: 'Roles',
      hideBelow: 'lg',
      sortValue: (u) => u.roles.map((r) => roleLabel[r] ?? r).join(', '),
      cell: (u) => (
        <div className="flex flex-wrap gap-1 min-w-[140px]">
          {u.roles.map((r) => (
            <span key={r} className={clsx('badge', ROLE_STYLE[r] ?? 'bg-muted')}>
              {roleLabel[r] ?? r}
            </span>
          ))}
        </div>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (u) => (
        <div className="flex justify-end gap-0.5">
          {u.canManage && (
            <>
              <button className="btn-icon" title="Reset password" onClick={() => setResetting(u)}>
                <KeyRound className="h-4 w-4" />
              </button>
              <button className="btn-icon" title="Edit" onClick={() => setEditing(u)}>
                <Pencil className="h-4 w-4" />
              </button>
              {!u.isSelf && (
                <button
                  className={clsx('btn-icon', u.disabled ? 'hover:text-success' : 'hover:text-destructive')}
                  title={u.disabled ? 'Enable' : 'Disable'}
                  onClick={() => setToggling(u)}
                >
                  {u.disabled ? <Power className="h-4 w-4" /> : <PowerOff className="h-4 w-4" />}
                </button>
              )}
            </>
          )}
          {u.agreementAccepted && (
            <button className="btn-icon" title="Download agreement acceptance" onClick={() => downloadCertificate(u)}>
              <Download className="h-4 w-4" />
            </button>
          )}
          <button className="btn-icon" title="History" onClick={() => setHistory(u)}>
            <History className="h-4 w-4" />
          </button>
          {u.canManage && !u.isSelf && (
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(u)}>
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
        title="Users"
        breadcrumbs={['Users']}
        actions={
          <button className="btn-primary" onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> New User
          </button>
        }
      />
      {users.isError && <ErrorBanner message={errorMessage(users.error)} />}
      <DataTable
        rows={users.data ?? []}
        columns={columns}
        rowKey={(u) => u.id}
        loading={users.isLoading}
        searchPlaceholder="Search users…"
        searchText={(u) => [u.groupName, u.email, u.username, u.firstName, u.lastName, u.phoneNumber, ...u.roles.map((r) => roleLabel[r] ?? r)].filter(Boolean).join(' ')}
        initialSort={{ key: 'groupName', dir: 'asc' }}
        rowClassName={(u) => clsx(u.disabled && 'text-muted-foreground')}
        toolbar={
          <SearchSelect
            className="w-full sm:w-72"
            options={groups.map((g) => ({ value: g.id, label: g.name }))}
            value={filterGroup}
            onChange={setFilterGroup}
            placeholder={`All my groups (${groups.length})`}
          />
        }
        emptyTitle="No users in this group"
        emptyDescription={me.roles.length ? 'Create a user to give someone access to Finish Genius.' : undefined}
      />

      {editing && (
        <UserFormModal user={editing === 'new' ? null : editing} defaultGroupId={filterGroup ?? groupId} onClose={() => setEditing(null)} />
      )}
      {resetting && <ResetPasswordModal user={resetting} onClose={() => setResetting(null)} />}
      {history && (
        <HistoryModal open onClose={() => setHistory(null)} entityType="User" entityId={history.id} title={`History — ${history.username}`} />
      )}
      <ConfirmDialog
        open={!!toggling}
        title={toggling?.disabled ? 'Enable User' : 'Disable User'}
        danger={!toggling?.disabled}
        confirmLabel={toggling?.disabled ? 'Enable' : 'Disable'}
        message={
          toggling?.disabled
            ? `Enable "${toggling?.username}"? They will be able to sign in again.`
            : `Disable "${toggling?.username}"? They will not be able to sign in until the account is enabled again.`
        }
        busy={toggle.isPending}
        onConfirm={() => toggling && toggle.mutate(toggling)}
        onClose={() => setToggling(null)}
      />
      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.username}" user?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}
