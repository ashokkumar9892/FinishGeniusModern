import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the WorkInstructions module.
export default function WorkInstructionsPage() {
  return (
    <>
      <PageHeader title="Work Instructions" breadcrumbs={['Work Instructions']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
