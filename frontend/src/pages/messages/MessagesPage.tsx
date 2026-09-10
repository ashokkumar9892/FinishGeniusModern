import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Messages module.
export default function MessagesPage() {
  return (
    <>
      <PageHeader title="DPM Center" breadcrumbs={['DPM Center']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
