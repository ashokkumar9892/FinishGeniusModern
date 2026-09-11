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
 * Keep in sync with backend Infrastructure/Access.cs.
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
} as const

export type ModuleKey = keyof typeof moduleRoles

export function hasRole(me: Me | undefined | null, ...roles: string[]) {
  return !!me && me.roles.some((r) => roles.includes(r))
}

export function canAccess(me: Me | undefined | null, module: ModuleKey) {
  return hasRole(me, ...moduleRoles[module])
}

export const isSystemAdmin = (me?: Me | null) => hasRole(me, SystemAdmin)
export const isAdmin = (me?: Me | null) => hasRole(me, SystemAdmin, GroupAdmin)
