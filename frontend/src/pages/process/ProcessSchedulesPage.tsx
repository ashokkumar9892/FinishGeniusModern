import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Process module.
export default function ProcessSchedulesPage() {
  return (
    <>
      <PageHeader title="Process Schedule List" breadcrumbs={['Process Schedule List']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
