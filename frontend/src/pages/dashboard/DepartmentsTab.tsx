import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { DataTable, type Column } from '@/components/DataTable'
import { SearchSelect } from '@/components/SearchSelect'
import { ConfirmDialog, ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import type { ApiMessage, DepartmentRow } from '@/pages/mywork/shared'

/** Dashboard › Department Management. */
export function DepartmentsTab({ search }: { search: string }) {
  const { groupId } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const [editing, setEditing] = useState<DepartmentRow | 'new' | null>(null)
  const [deleting, setDeleting] = useState<DepartmentRow | null>(null)

  const q = useQuery({
    queryKey: ['departments', groupId, search],
    queryFn: () => api.get<DepartmentRow[]>('/departments', { params: { groupId, search: search || undefined } }).then((r) => r.data),
    enabled: groupId > 0,
  })

  const remove = useMutation({
    mutationFn: (d: DepartmentRow) => api.delete<ApiMessage>(`/departments/${d.id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      invalidateDepartments(qc)
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setDeleting(null)
    },
  })

  const columns: Column<DepartmentRow>[] = [
    { key: 'id', header: '#', className: 'w-16' },
    { key: 'name', header: 'Name', cell: (d) => <span className="font-medium">{d.name}</span> },
    { key: 'groupName', header: 'Group', hideBelow: 'sm' },
    { key: 'scheduleCount', header: 'Schedules', align: 'right', hideBelow: 'sm' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (d) => (
        <div className="flex justify-end gap-0.5">
          <button className="btn-icon" title="Edit" onClick={() => setEditing(d)}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(d)}>
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <>
      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}
      <DataTable
        rows={q.data ?? []}
        columns={columns}
        rowKey={(d) => d.id}
        loading={q.isLoading}
        initialSort={{ key: 'id', dir: 'asc' }}
        searchPlaceholder="Search departments…"
        emptyTitle={search ? 'No departments match' : 'No departments yet'}
        emptyDescription={search ? undefined : 'Departments group process schedules into panels on the My Work Processes tab.'}
        toolbarRight={
          <button className="btn-primary" onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> Add Department
          </button>
        }
      />
      {editing && <DepartmentModal key={editing === 'new' ? 'new' : editing.id} department={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={!!deleting}
        busy={remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
        message={
          deleting && (
            <>
              Are you sure you want to delete the "{deleting.name}" department?
              {deleting.scheduleCount > 0 && (
                <span className="block mt-2 text-amber-700 dark:text-amber-400">
                  {deleting.scheduleCount} process schedule{deleting.scheduleCount === 1 ? ' is' : 's are'} assigned to it — reassign them first.
                </span>
              )}
            </>
          )
        }
      />
    </>
  )
}

function invalidateDepartments(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ['departments'] })
  qc.invalidateQueries({ queryKey: ['dashboard-departments'] })
  qc.invalidateQueries({ queryKey: ['my-work-processes'] })
}

function DepartmentModal({ department, onClose }: { department: DepartmentRow | null; onClose: () => void }) {
  const { groupId: current, groups } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const [groupId, setGroupId] = useState<number>(department?.groupId ?? current)
  const [name, setName] = useState(department?.name ?? '')
  const [nameError, setNameError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      (department ? api.put<ApiMessage>(`/departments/${department.id}`, { groupId, name }) : api.post<ApiMessage>('/departments', { groupId, name })).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      invalidateDepartments(qc)
      onClose()
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const submit = () => {
    setServerError(null)
    if (!name.trim()) return setNameError('Department Name is required.')
    save.mutate()
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={department ? 'Edit Department' : 'Add New Department'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <ErrorBanner message={serverError} />
        <Field label="Group" required>
          <SearchSelect clearable={false} options={groups.map((g) => ({ value: g.id, label: g.name }))} value={groupId} onChange={(v) => v && setGroupId(v)} />
        </Field>
        <Field label="Department Name" required error={nameError}>
          <input
            className={nameError ? 'input input-invalid' : 'input'}
            value={name}
            maxLength={200}
            autoFocus
            onChange={(e) => {
              setName(e.target.value)
              setNameError(null)
            }}
          />
        </Field>
      </form>
    </Modal>
  )
}
