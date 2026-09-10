import { PageHeader, Tabs } from '@/components/ui'
import { useHashTab } from './shared'
import { ProgressTab } from './ProgressTab'
import { ManagementTab } from './ManagementTab'

const TABS = ['workProgress', 'processManagement'] as const
type TabKey = (typeof TABS)[number]

export default function MyWorkPage() {
  const [tab, setTab] = useHashTab<TabKey>(TABS, 'workProgress')
  return (
    <>
      <PageHeader title="My Work" breadcrumbs={['My Work']} />
      <Tabs<TabKey>
        className="mb-4"
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'workProgress', label: 'My Work Progress' },
          { key: 'processManagement', label: 'Processes Management' },
        ]}
      />
      {tab === 'workProgress' ? <ProgressTab /> : <ManagementTab />}
    </>
  )
}
