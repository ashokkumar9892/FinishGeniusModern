import { useVisibleTab } from '@/lib/access'
import { PageHeader, Tabs } from '@/components/ui'
import { useHashTab } from './shared'
import { ProgressTab } from './ProgressTab'
import { ManagementTab } from './ManagementTab'

const TABS = ['workProgress', 'processManagement'] as const
type TabKey = (typeof TABS)[number]

export default function MyWorkPage() {
  const [requestedTab, setTab] = useHashTab<TabKey>(TABS, 'workProgress')
  const { allowed, tab } = useVisibleTab('myWork', TABS, requestedTab)
  return (
    <>
      <PageHeader title="My Work" breadcrumbs={['My Work']} />
      {tab && (
        <Tabs<TabKey>
          className="mb-4"
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'workProgress', label: 'My Work Progress', hidden: !allowed('workProgress') },
            { key: 'processManagement', label: 'Processes Management', hidden: !allowed('processManagement') },
          ]}
        />
      )}
      {tab === 'workProgress' && <ProgressTab />}
      {tab === 'processManagement' && <ManagementTab />}
    </>
  )
}
