import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Groups module.
export default function GroupsPage() {
  return (
    <>
      <PageHeader title="Groups" breadcrumbs={['Groups']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
