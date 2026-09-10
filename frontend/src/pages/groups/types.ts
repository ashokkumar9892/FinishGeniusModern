export interface GroupRow {
  id: number
  name: string
  address1?: string | null
  address2?: string | null
  city?: string | null
  state?: string | null
  zip?: string | null
  country?: string | null
  timeZone: string
  timeZoneLabel: string
  apiKey?: string | null
  logoFile?: string | null
  checklistDeletionEnabled: boolean
  createdAt: string
  updatedAt?: string | null
  isDefault: boolean
  canEdit: boolean
  canDelete: boolean
  canCopy: boolean
}

export interface CopyPreview {
  id: number
  name: string
  suggestedName: string
  items: { key: string; label: string; count: number }[]
}

export const DEFAULT_TIME_ZONE = 'Eastern Standard Time'
