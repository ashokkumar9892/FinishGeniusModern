import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Dashboard module.
export default function DashboardPage() {
  return (
    <>
      <PageHeader title="Dashboard" breadcrumbs={['Dashboard']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
