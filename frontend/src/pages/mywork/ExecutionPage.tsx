import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the MyWork module.
export default function ExecutionPage() {
  return (
    <>
      <PageHeader title="My Work Process" breadcrumbs={['My Work Process']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
