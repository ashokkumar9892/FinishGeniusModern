import clsx from 'clsx'
import { hasRole, Roles } from '@/lib/access'
import type { Me } from '@/lib/types'

export interface WiRow {
  id: number
  groupId: number
  groupName: string
  documentNumber: string
  name: string
  issueDate?: string | null
  createdAt?: string | null
  updatedAt?: string | null
  status: string
  isReleased: boolean
  version: number
  stepCount: number
}

export interface WiMedia {
  id: number
  fileName: string
  storedFile: string
  contentType: string
  isVideo: boolean
}

export interface WiStep {
  id: number
  level: number
  title: string
  body?: string | null
  media: WiMedia[]
}

export interface WiTrail {
  id: number
  version: string
  author: string
  log?: string | null
  date: string
}

export interface WiRelatedDoc {
  id: number
  documentNumber: string
  documentName: string
  author?: string | null
}

export interface WiSignature {
  id: number
  name: string
  position?: string | null
  date: string
}

export type WiItemKind = 'Equipment' | 'Material'

export interface WiItem {
  id: number
  kind: WiItemKind
  description: string
  materialId?: number | null
  materialName?: string | null
}

export interface WiDoc {
  id: number
  groupId: number
  groupName?: string | null
  documentNumber: string
  name: string
  issueDate?: string | null
  version: number
  isReleased: boolean
  status: string
  controlled: boolean
  location?: string | null
  purpose?: string | null
  scope?: string | null
  terminology?: string | null
  createdAt: string
  updatedAt?: string | null
  trail: WiTrail[]
  relatedDocuments: WiRelatedDoc[]
  signatures: WiSignature[]
  items: WiItem[]
  steps: WiStep[]
}

export interface ApiMessage {
  message: string
  id?: number
}

/** Allowed step media (backend FileStorage.MediaExtensions). */
export const MEDIA_EXT = ['jpg', 'jpeg', 'png', 'gif', 'mp4', 'mov']

/** Legacy "Delete Instruction" was broken; admins and FG Pro users may delete (backend enforces the same roles). */
export const canDeleteWorkInstruction = (me?: Me | null) => hasRole(me, Roles.SystemAdmin, Roles.GroupAdmin, Roles.FGPro)

export function StatusBadge({ status, className }: { status: string; className?: string }) {
  const released = status.toUpperCase().startsWith('RELEASED')
  return (
    <span
      className={clsx('badge-status', released ? 'badge-status-ok' : 'badge-status-warn', className)}
    >
      {status}
    </span>
  )
}
