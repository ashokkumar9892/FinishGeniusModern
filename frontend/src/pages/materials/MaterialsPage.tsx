import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Materials module.
export default function MaterialsPage() {
  return (
    <>
      <PageHeader title="Equipment & Materials List" breadcrumbs={['Equipment & Materials List']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
