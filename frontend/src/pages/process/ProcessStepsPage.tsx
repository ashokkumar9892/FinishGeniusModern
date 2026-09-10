import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Process module.
export default function ProcessStepsPage() {
  return (
    <>
      <PageHeader title="Process Step List" breadcrumbs={['Process Step List']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
