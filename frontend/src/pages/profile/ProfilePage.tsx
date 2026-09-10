import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Profile module.
export default function ProfilePage() {
  return (
    <>
      <PageHeader title="Profile" breadcrumbs={['Profile']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
