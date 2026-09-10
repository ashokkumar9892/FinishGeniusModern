import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { FolderOpen, Play } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { Card, EmptyState, ErrorBanner, LoadingBlock, Spinner } from '@/components/ui'
import { useStartProcess } from '@/pages/mywork/shared'

interface PanelSchedule {
  id: number
  groupId: number
  groupName: string
  name: string
  number: string
  departmentId?: number | null
  lastRun?: string | null
  runs: number
  activeExecutionId?: number | null
}

interface Panel {
  id: number | null
  name: string
  groupId: number
  groupName: string
  schedules: PanelSchedule[]
}

/** Dashboard › My Work Processes: one panel per department with Start / Resume. */
export function ProcessesTab({ search }: { search: string }) {
  const { groupId } = useGroup()
  const q = useQuery({
    queryKey: ['dashboard-departments', groupId, search],
    queryFn: () => api.get<Panel[]>('/dashboard/departments', { params: { groupId, search: search || undefined } }).then((r) => r.data),
    enabled: groupId > 0,
  })

  if (q.isLoading) return <LoadingBlock />
  if (q.isError) return <ErrorBanner message={errorMessage(q.error)} />
  if (!q.data?.length)
    return (
      <div className="card">
        <EmptyState
          title="No departments match"
          icon={<FolderOpen className="h-5 w-5" />}
          description={
            search
              ? `No department names contain "${search}".`
              : 'Create departments in the Department Management tab, then assign process schedules to them in My Work › Processes Management.'
          }
        />
      </div>
    )

  return (
    <div className="grid gap-4 xl:grid-cols-2">
      {q.data.map((p) => (
        <DepartmentPanel key={p.id ?? 'unassigned'} panel={p} />
      ))}
    </div>
  )
}

function DepartmentPanel({ panel }: { panel: Panel }) {
  const navigate = useNavigate()
  const start = useStartProcess()

  const columns: Column<PanelSchedule>[] = [
    { key: 'id', header: '#', className: 'w-14' },
    { key: 'groupName', header: 'Group', hideBelow: 'md' },
    {
      key: 'lastRun',
      header: 'Last Run',
      sortValue: (r) => r.lastRun ?? '',
      cell: (r) => (r.lastRun ? <span className="whitespace-nowrap">{dateTime(r.lastRun)}</span> : <span className="text-muted-foreground">Never</span>),
    },
    {
      key: 'name',
      header: 'Process Schedule Name',
      cell: (r) => (
        <div className="min-w-0">
          <div className="font-medium">{r.name}</div>
          <div className="text-xs text-muted-foreground">
            #{r.number} · {r.runs} run{r.runs === 1 ? '' : 's'}
          </div>
        </div>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) =>
        r.activeExecutionId ? (
          <button className="btn-success btn-sm" onClick={() => navigate(`/my-work/${r.activeExecutionId}`)} title="Resume your process in progress">
            <Play className="h-3.5 w-3.5 fill-current" /> Resume
          </button>
        ) : (
          <button className="btn-primary btn-sm" disabled={start.isPending} onClick={() => start.mutate(r.id)} title="Start this process">
            {start.isPending && start.variables === r.id ? <Spinner /> : <Play className="h-3.5 w-3.5 fill-current" />} Start
          </button>
        ),
    },
  ]

  return (
    <Card
      title={
        <span className={panel.id == null ? 'text-muted-foreground' : undefined}>
          {panel.name} ({panel.groupName})
        </span>
      }
      actions={<span className="badge bg-muted text-muted-foreground">{panel.schedules.length}</span>}
      className="min-w-0"
    >
      <DataTable
        bare
        dense
        rows={panel.schedules}
        columns={columns}
        rowKey={(r) => r.id}
        pageSize={10}
        initialSort={{ key: 'lastRun', dir: 'desc' }}
        searchPlaceholder="Process Search"
        searchText={(r) => `${r.id} ${r.name} ${r.number} ${r.groupName}`}
        emptyTitle="No process schedules in this department"
        emptyDescription="Assign schedules in My Work › Processes Management."
      />
    </Card>
  )
}
