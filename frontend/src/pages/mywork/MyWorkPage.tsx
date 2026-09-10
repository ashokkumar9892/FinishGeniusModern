import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the MyWork module.
export default function MyWorkPage() {
  return (
    <>
      <PageHeader title="My Work" breadcrumbs={['My Work']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
