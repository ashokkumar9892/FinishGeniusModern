export interface GroupRef {
  id: number
  name: string
  logoFile?: string | null
}

export interface Me {
  id: number
  username: string
  email: string
  firstName?: string | null
  lastName?: string | null
  phoneNumber?: string | null
  groupId: number
  defaultGroupId: number
  agreementAccepted: boolean
  roles: string[]
  groups: GroupRef[]
  unreadMessages: number
}

export interface Option<T = string | number> {
  value: T
  label: string
  sub?: string
}

export interface Lookups {
  roles: { value: string; label: string }[]
  materialTypes: { value: number; label: string }[]
  deviceTypes: { value: number; label: string }[]
  industrySectors: { id: number; name: string }[]
  inputTypes: string[]
  calcVariables: { value: string; label: string }[]
  timeZones: { value: string; label: string }[]
  groups: { id: number; name: string }[]
}

/** Values match the backend MaterialType enum (and the legacy FGAPP database). */
export const MaterialType = {
  Base: 1,
  Pigment: 2,
  Dye: 3,
  Equipment: 4,
  Sundry: 5,
  Formula: 6,
  Product: 7,
} as const
export type MaterialTypeValue = (typeof MaterialType)[keyof typeof MaterialType]

export const materialTypeLabel: Record<number, string> = {
  1: 'Base',
  2: 'Pigment',
  3: 'Dye',
  4: 'Equipment',
  5: 'Sundry',
  6: 'Formula',
  7: 'Product',
}

/** Entity types a document can be linked to (backend LinkEntityTypes). */
export type LinkEntityType = 'Material' | 'Formula' | 'ProcessStep' | 'ProcessSchedule' | 'Pricing' | 'MaterialQuantity'
