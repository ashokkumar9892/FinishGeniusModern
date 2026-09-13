import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Search } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useVisibleTab } from '@/lib/access'
import { useGroup } from '@/lib/auth'
import { ErrorBanner, PageHeader, SearchInput, Tabs } from '@/components/ui'
import { useHashTab } from '@/pages/mywork/shared'
import { DefectsDonut, ExecutionsChart, KpiRow, TopSchedules, type DashboardSummary } from './charts'
import { ProcessesTab } from './ProcessesTab'
import { DevicesTab } from './DevicesTab'
import { DepartmentsTab } from './DepartmentsTab'

const TABS = ['processList', 'deviceList', 'departmentManagement'] as const
type TabKey = (typeof TABS)[number]

export default function DashboardPage() {
  const { groupId } = useGroup()
  const [requestedTab, setTab] = useHashTab<TabKey>(TABS, 'processList')
  const { allowed, tab } = useVisibleTab('dashboard', TABS, requestedTab)
  const [draft, setDraft] = useState('')
  const [search, setSearch] = useState('')

  const summary = useQuery({
    queryKey: ['dashboard-summary', groupId],
    queryFn: () =>
      api.get<DashboardSummary>('/dashboard/summary', { params: { groupId, tzOffset: new Date().getTimezoneOffset() } }).then((r) => r.data),
    enabled: groupId > 0,
    refetchInterval: 60_000,
  })

  return (
    <>
      <PageHeader title="Dashboard" breadcrumbs={['Dashboard']} />

      <form
        className="card p-3 sm:p-4 mb-4 flex flex-col sm:flex-row sm:items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          setSearch(draft.trim())
        }}
      >
        <div className="flex-1 max-w-xl">
          <label className="label">Department Search</label>
          <SearchInput
            value={draft}
            onChange={(v) => {
              setDraft(v)
              if (!v) setSearch('')
            }}
            placeholder="Search by department name…"
          />
        </div>
        <button type="submit" className="btn-primary">
          <Search className="h-4 w-4" /> Search
        </button>
      </form>

      {summary.isError && <ErrorBanner message={errorMessage(summary.error)} />}
      <div className="space-y-4 mb-6">
        <KpiRow data={summary.data} loading={summary.isLoading} />
        <div className="grid gap-4 lg:grid-cols-2 xl:grid-cols-4">
          <ExecutionsChart data={summary.data} />
          <DefectsDonut data={summary.data} />
          <TopSchedules data={summary.data} />
        </div>
      </div>

      {tab && (
        <Tabs<TabKey>
          className="mb-4"
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'processList', label: 'My Work Processes', hidden: !allowed('processList') },
            { key: 'deviceList', label: 'Devices', hidden: !allowed('deviceList') },
            { key: 'departmentManagement', label: 'Department Management', hidden: !allowed('departmentManagement') },
          ]}
        />
      )}
      {tab === 'processList' && <ProcessesTab search={search} />}
      {tab === 'deviceList' && <DevicesTab />}
      {tab === 'departmentManagement' && <DepartmentsTab search={search} />}
    </>
  )
}
