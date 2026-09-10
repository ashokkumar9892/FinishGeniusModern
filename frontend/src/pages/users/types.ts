export interface UserRow {
  id: number
  groupId: number
  groupName?: string | null
  email: string
  username: string
  firstName?: string | null
  lastName?: string | null
  phoneNumber?: string | null
  disabled: boolean
  agreementAccepted: boolean
  agreementAcceptedAt?: string | null
  createdAt: string
  lastLoginAt?: string | null
  roles: string[]
  extraGroupIds: number[]
  isSelf: boolean
  canManage: boolean
}

/** Fallback labels (the server's /lookups roles are preferred). */
export const ROLE_LABELS: Record<string, string> = {
  FGPro: 'Finish Genius Pro',
  FGProPlus: 'Finish Genius Pro+',
  GroupAdmin: 'Group Administrator',
  SupportAgent: 'FG Support Agent',
  SystemAdmin: 'System Administrator',
}

export const PRIVILEGED_ROLES = ['SystemAdmin', 'SupportAgent']
