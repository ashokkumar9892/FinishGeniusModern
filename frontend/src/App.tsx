import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth, GroupProvider } from '@/lib/auth'
import { canAccess, type ModuleKey } from '@/lib/access'
import { Layout, navSections } from '@/components/Layout'
import { EmptyState, LoadingBlock } from '@/components/ui'
import LoginPage from '@/pages/LoginPage'

const GroupsPage = lazy(() => import('@/pages/groups/GroupsPage'))
const UsersPage = lazy(() => import('@/pages/users/UsersPage'))
const ProfilePage = lazy(() => import('@/pages/profile/ProfilePage'))
const MessagesPage = lazy(() => import('@/pages/messages/MessagesPage'))
const PhotoGalleryPage = lazy(() => import('@/pages/photos/PhotoGalleryPage'))
const MaterialsPage = lazy(() => import('@/pages/materials/MaterialsPage'))
const MaterialCategoriesPage = lazy(() => import('@/pages/materials/MaterialCategoriesPage'))
const ImportPage = lazy(() => import('@/pages/materials/ImportPage'))
const FormulasPage = lazy(() => import('@/pages/formulas/FormulasPage'))
const FormulaEditPage = lazy(() => import('@/pages/formulas/FormulaEditPage'))
const FormulaCalculatorPage = lazy(() => import('@/pages/formulas/FormulaCalculatorPage'))
const SubStepsPage = lazy(() => import('@/pages/process/SubStepsPage'))
const ProcessStepsPage = lazy(() => import('@/pages/process/ProcessStepsPage'))
const ProcessStepBuilderPage = lazy(() => import('@/pages/process/ProcessStepBuilderPage'))
const ProcessSchedulesPage = lazy(() => import('@/pages/process/ProcessSchedulesPage'))
const ScheduleEditPage = lazy(() => import('@/pages/process/ScheduleEditPage'))
const MaterialQuantitiesPage = lazy(() => import('@/pages/process/MaterialQuantitiesPage'))
const PricingPage = lazy(() => import('@/pages/process/PricingPage'))
const MyWorkPage = lazy(() => import('@/pages/mywork/MyWorkPage'))
const ExecutionPage = lazy(() => import('@/pages/mywork/ExecutionPage'))
const DashboardPage = lazy(() => import('@/pages/dashboard/DashboardPage'))
const WorkInstructionsPage = lazy(() => import('@/pages/workinstructions/WorkInstructionsPage'))
const WorkInstructionDocPage = lazy(() => import('@/pages/workinstructions/WorkInstructionDocPage'))
const SystemSettingsPage = lazy(() => import('@/pages/settings/SystemSettingsPage'))
const PageAccessPage = lazy(() => import('@/pages/access/PageAccessPage'))

function Guard({ module, children }: { module: ModuleKey; children: ReactNode }) {
  const { me } = useAuth()
  if (!canAccess(me, module))
    return <EmptyState title="Access denied" description="Your role does not include this module. Contact your administrator if you need access." />
  return <>{children}</>
}

function Home() {
  const { me } = useAuth()
  const first = navSections.flatMap((s) => s.items).find((i) => canAccess(me, i.module))
  return <Navigate to={first?.to ?? '/groups'} replace />
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { me, loading } = useAuth()
  const location = useLocation()
  if (loading) return <LoadingBlock label="Starting Finish Genius…" />
  if (!me) return <Navigate to="/login" replace state={{ from: location }} />
  return <GroupProvider>{children}</GroupProvider>
}

const g = (module: ModuleKey, el: ReactNode) => <Guard module={module}>{el}</Guard>

export default function App() {
  return (
    <Suspense fallback={<LoadingBlock />}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          element={
            <RequireAuth>
              <Layout />
            </RequireAuth>
          }
        >
          <Route index element={<Home />} />
          <Route path="groups" element={g('groups', <GroupsPage />)} />
          <Route path="users" element={g('users', <UsersPage />)} />
          <Route path="profile" element={<ProfilePage />} />
          <Route path="messages" element={g('messages', <MessagesPage />)} />
          <Route path="photos" element={g('photos', <PhotoGalleryPage />)} />
          <Route path="materials" element={g('materials', <MaterialsPage />)} />
          <Route path="material-categories" element={g('materialCategories', <MaterialCategoriesPage />)} />
          <Route path="import" element={g('import', <ImportPage />)} />
          <Route path="formulas" element={g('formulas', <FormulasPage />)} />
          <Route path="formulas/:id" element={g('formulas', <FormulaEditPage />)} />
          <Route path="formulas/:id/calculator" element={g('formulas', <FormulaCalculatorPage />)} />
          <Route path="sub-steps" element={g('subSteps', <SubStepsPage />)} />
          <Route path="process-steps" element={g('processSteps', <ProcessStepsPage />)} />
          <Route path="process-steps/:id" element={g('processSteps', <ProcessStepBuilderPage />)} />
          <Route path="process-schedules" element={g('processSchedules', <ProcessSchedulesPage />)} />
          <Route path="process-schedules/:id" element={g('processSchedules', <ScheduleEditPage />)} />
          <Route path="material-quantities" element={g('materialQuantities', <MaterialQuantitiesPage />)} />
          <Route path="pricing" element={g('pricing', <PricingPage />)} />
          <Route path="my-work" element={g('myWork', <MyWorkPage />)} />
          <Route path="my-work/:id" element={g('myWork', <ExecutionPage />)} />
          <Route path="dashboard" element={g('dashboard', <DashboardPage />)} />
          <Route path="work-instructions" element={g('workInstructions', <WorkInstructionsPage />)} />
          <Route path="work-instructions/:id" element={g('workInstructions', <WorkInstructionDocPage />)} />
          <Route path="settings" element={g('settings', <SystemSettingsPage />)} />
          <Route path="access" element={g('access', <PageAccessPage />)} />
          <Route path="*" element={<EmptyState title="Page not found" description="The page you requested does not exist." />} />
        </Route>
      </Routes>
    </Suspense>
  )
}
