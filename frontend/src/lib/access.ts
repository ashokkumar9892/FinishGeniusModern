import { useAuth } from './auth'
import type { Me } from './types'

export const Roles = {
  SystemAdmin: 'SystemAdmin',
  SupportAgent: 'SupportAgent',
  GroupAdmin: 'GroupAdmin',
  FGPro: 'FGPro',
  FGProPlus: 'FGProPlus',
} as const

const { SystemAdmin, SupportAgent, GroupAdmin, FGPro, FGProPlus } = Roles

/**
 * Menu access matrix from the AWFI "2. Users" test document.
 * Keep in sync with backend Infrastructure/Access.cs and Infrastructure/PageAccess.cs (page keys).
 */
export const moduleRoles = {
  groups: [FGPro, FGProPlus, GroupAdmin, SupportAgent, SystemAdmin],
  users: [GroupAdmin, SystemAdmin],
  photos: [FGPro, FGProPlus, GroupAdmin, SupportAgent, SystemAdmin],
  materials: [FGPro, GroupAdmin, SystemAdmin],
  formulas: [FGPro, GroupAdmin, SystemAdmin],
  processSteps: [FGPro, GroupAdmin, SystemAdmin],
  processSchedules: [FGPro, GroupAdmin, SystemAdmin],
  materialQuantities: [FGPro, GroupAdmin, SystemAdmin],
  pricing: [FGPro, GroupAdmin, SystemAdmin],
  myWork: [FGProPlus, GroupAdmin, SystemAdmin],
  dashboard: [FGProPlus, GroupAdmin, SystemAdmin],
  workInstructions: [FGPro, FGProPlus, GroupAdmin, SystemAdmin],
  materialCategories: [GroupAdmin, SystemAdmin],
  subSteps: [SystemAdmin],
  import: [GroupAdmin, SystemAdmin],
  messages: [FGPro, FGProPlus, GroupAdmin, SupportAgent, SystemAdmin],
  settings: [SystemAdmin],
  /** Page Access: no role — only the owner account, which bypasses this matrix. */
  access: [],
  /** Login Activity (sign-in log): owner account only, like Page Access. */
  loginActivity: [],
} as const

export type ModuleKey = keyof typeof moduleRoles

export function hasRole(me: Me | undefined | null, ...roles: string[]) {
  return !!me && me.roles.some((r) => roles.includes(r))
}

/** The role matrix minus the pages the owner turned off (Page Access). The owner account sees every page. */
export function canAccess(me: Me | undefined | null, module: ModuleKey) {
  if (!me) return false
  if (me.isOwner) return true
  return hasRole(me, ...moduleRoles[module]) && !me.hiddenPages?.includes(module)
}

/** False when the owner turned this tab of the page off for the user's roles. */
export function canSeeTab(me: Me | undefined | null, page: ModuleKey, tab: string) {
  if (!me) return false
  return !!me.isOwner || !me.hiddenTabs?.includes(`${page}.${tab}`)
}

/** The tab to show: the requested one when it is on for this user, else the first one that is (undefined = none). */
export function useVisibleTab<K extends string>(page: ModuleKey, keys: readonly K[], requested: K) {
  const { me } = useAuth()
  const allowed = (k: K) => canSeeTab(me, page, k)
  return { allowed, tab: allowed(requested) ? requested : keys.find(allowed) }
}

export const isOwner = (me?: Me | null) => !!me?.isOwner
export const isSystemAdmin = (me?: Me | null) => hasRole(me, SystemAdmin)
export const isAdmin = (me?: Me | null) => hasRole(me, SystemAdmin, GroupAdmin)
