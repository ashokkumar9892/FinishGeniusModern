import { Modal } from './ui'
import type { LinkEntityType } from '@/lib/types'

/**
 * "Documents" row action shared by Materials, Formulas, Process Steps, Schedules, Pricing and
 * Material Quantities: lists documents linked to one object and lets the user upload/link/unlink.
 *
 * STUB — the Documents module implements this component (same props).
 */
export function EntityDocuments({
  open,
  onClose,
  entityType,
  entityId,
  groupId,
  title,
}: {
  open: boolean
  onClose: () => void
  entityType: LinkEntityType
  /** 0 for page-level documents (e.g. Pricing / Material Quantities of a group). */
  entityId: number
  groupId: number
  title?: string
}) {
  return (
    <Modal open={open} onClose={onClose} title={title ?? 'Documents'}>
      <p className="text-sm text-muted-foreground">
        Documents for {entityType} #{entityId} (group {groupId}).
      </p>
    </Modal>
  )
}
