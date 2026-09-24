/**
 * Each page is its own script file (see App.tsx), which the browser normally only starts downloading once the
 * session check has finished. Asking for it here, while /auth/me is still on the wire, takes that wait off the
 * opening of every page; hovering a menu item does the same for the page the user is about to click.
 * The imports are the same module ids App.tsx uses, so this adds no extra files to the build.
 */
const pages: Record<string, () => Promise<unknown>> = {
  '/groups': () => import('@/pages/groups/GroupsPage'),
  '/users': () => import('@/pages/users/UsersPage'),
  '/profile': () => import('@/pages/profile/ProfilePage'),
  '/messages': () => import('@/pages/messages/MessagesPage'),
  '/photos': () => import('@/pages/photos/PhotoGalleryPage'),
  '/materials': () => import('@/pages/materials/MaterialsPage'),
  '/material-categories': () => import('@/pages/materials/MaterialCategoriesPage'),
  '/import': () => import('@/pages/materials/ImportPage'),
  '/formulas': () => import('@/pages/formulas/FormulasPage'),
  '/color-matching': () => import('@/pages/colors/ColorMatchingPage'),
  '/sub-steps': () => import('@/pages/process/SubStepsPage'),
  '/process-steps': () => import('@/pages/process/ProcessStepsPage'),
  '/process-schedules': () => import('@/pages/process/ProcessSchedulesPage'),
  '/material-quantities': () => import('@/pages/process/MaterialQuantitiesPage'),
  '/pricing': () => import('@/pages/process/PricingPage'),
  '/my-work': () => import('@/pages/mywork/MyWorkPage'),
  '/dashboard': () => import('@/pages/dashboard/DashboardPage'),
  '/work-instructions': () => import('@/pages/workinstructions/WorkInstructionsPage'),
  '/settings': () => import('@/pages/settings/SystemSettingsPage'),
  '/access': () => import('@/pages/access/PageAccessPage'),
  '/login-activity': () => import('@/pages/access/LoginActivityPage'),
}

/** Pages opened from a list row (…/:id) carry their own code. */
const details: Record<string, () => Promise<unknown>> = {
  '/formulas': () => import('@/pages/formulas/FormulaEditPage'),
  '/process-steps': () => import('@/pages/process/ProcessStepBuilderPage'),
  '/process-schedules': () => import('@/pages/process/ScheduleEditPage'),
  '/my-work': () => import('@/pages/mywork/ExecutionPage'),
  '/work-instructions': () => import('@/pages/workinstructions/WorkInstructionDocPage'),
}

const started = new Set<string>()

/** Starts downloading the code for a path (the list page, or the detail page for "/formulas/12"). Safe to call often. */
export function preloadRoute(pathname: string) {
  const segments = pathname.replace(import.meta.env.BASE_URL, '/').split('/').filter(Boolean)
  const base = '/' + (segments[0] ?? '')
  const load = (segments.length > 1 && details[base]) || pages[base]
  if (!load || started.has(pathname)) return
  started.add(pathname)
  load().catch(() => started.delete(pathname)) // a failed prefetch must not break the page; React retries on render
}
