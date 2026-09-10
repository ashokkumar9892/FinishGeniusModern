import { Modal } from './ui'

/**
 * Central document library ("+ Docs" on Equipment & Materials): list / create / edit / copy / link / delete
 * documents of a group.
 *
 * STUB — the Documents module implements this component (same props).
 */
export function DocumentLibraryModal({ open, onClose, groupId }: { open: boolean; onClose: () => void; groupId: number }) {
  return (
    <Modal open={open} onClose={onClose} title="Documents" size="xl">
      <p className="text-sm text-muted-foreground">Document library for group {groupId}.</p>
    </Modal>
  )
}
