import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Users module.
export default function UsersPage() {
  return (
    <>
      <PageHeader title="Users" breadcrumbs={['Users']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
