import { PageHeader, EmptyState } from '@/components/ui'

// Placeholder — implemented by the Photos module.
export default function PhotoGalleryPage() {
  return (
    <>
      <PageHeader title="Photo Gallery" breadcrumbs={['Photo Gallery']} />
      <EmptyState title="Coming soon" />
    </>
  )
}
