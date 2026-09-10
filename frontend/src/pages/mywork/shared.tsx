import { useCallback } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useToast } from '@/components/toast'

// ---------------------------------------------------------------------------
// Types (backend: MyWorkController / DepartmentsController)
// ---------------------------------------------------------------------------

export const ExecutionStatus = { InProgress: 0, Completed: 1, Cancelled: 2 } as const
export type ExecutionStatusValue = (typeof ExecutionStatus)[keyof typeof ExecutionStatus]
export type StatusFilter = 'InProgress' | 'Completed' | 'Cancelled' | 'all'

export interface ExecutionRow {
  id: number
  groupId: number
  groupName: string
  scheduleId: number
  scheduleName: string
  scheduleNumber: string
  userId: number
  userName: string
  startedAt: string
  completedAt?: string | null
  status: ExecutionStatusValue
  statusLabel: string
  ordering: number
  totalLines: number
  checkedLines: number
  progress: number
  defectCount: number
  adderCount: number
}

export interface ExecutionCheck {
  id: number
  userId: number
  userName?: string | null
  checkedAt: string
  recordedValue?: string | null
}

export interface ExecutionLine {
  id: number
  sequence: number
  description: string
  value?: string | null
  unit?: string | null
  minValue?: number | null
  maxValue?: number | null
  checks: ExecutionCheck[]
  outOfRange: boolean
}

export interface ExecutionStep {
  stepNumber: number
  stepName: string
  lines: ExecutionLine[]
}

export interface ExecutionDetail {
  id: number
  groupId: number
  groupName: string
  checklistDeletionEnabled: boolean
  scheduleId: number
  scheduleName: string
  scheduleNumber: string
  customerName?: string | null
  userId: number
  userName: string
  status: ExecutionStatusValue
  statusLabel: string
  notes?: string | null
  startedAt: string
  completedAt?: string | null
  totalLines: number
  checkedLines: number
  defectCount: number
  adderCount: number
  canUndoAny: boolean
  steps: ExecutionStep[]
}

export interface DefectType {
  id: number
  groupId: number
  name: string
  chartColor?: string | null
  isArchived: boolean
  usageCount: number
}

export interface AdderType {
  id: number
  groupId: number
  name: string
  isArchived: boolean
  usageCount: number
}

export interface DefectRow {
  id: number
  defectTypeId: number
  defectTypeName: string
  chartColor?: string | null
  quantity: number
  notes?: string | null
  userId: number
  userName?: string | null
  createdAt: string
}

export interface AdderRow {
  id: number
  adderTypeId: number
  adderTypeName: string
  value: string
  userId: number
  userName?: string | null
  createdAt: string
}

export type HistoryKind =
  | 'started' | 'checked' | 'unchecked' | 'defect' | 'defectRemoved' | 'adder' | 'adderRemoved' | 'completed' | 'cancelled' | 'other'

export interface HistoryEvent {
  id: number
  kind: HistoryKind
  action: string
  details?: string | null
  userName?: string | null
  createdAt: string
}

export interface ProcessRow {
  id: number
  groupId: number
  groupName: string
  name: string
  number: string
  customerName?: string | null
  departmentId?: number | null
  departmentName?: string | null
  stepCount: number
  runs: number
  activeRuns: number
  lastRun?: string | null
}

export interface DepartmentRow {
  id: number
  groupId: number
  groupName: string
  name: string
  scheduleCount: number
}

/** GET /api/process-schedules (owned by the Process module). */
export interface ScheduleRef {
  id: number
  groupId: number
  groupName: string
  name: string
  number: string
  customerName?: string | null
  departmentId?: number | null
  departmentName?: string | null
  isArchived: boolean
  stepCount: number
}

export interface ApiMessage {
  message: string
  id?: number
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

/** Tab state kept in the URL hash (e.g. `#workProgress`) so links and refreshes land on the same tab. */
export function useHashTab<K extends string>(keys: readonly K[], fallback: K): [K, (k: K) => void] {
  const location = useLocation()
  const navigate = useNavigate()
  const current = location.hash.replace(/^#/, '') as K
  const value = keys.includes(current) ? current : fallback
  const set = useCallback(
    (k: K) => navigate({ pathname: location.pathname, search: location.search, hash: `#${k}` }, { replace: true }),
    [navigate, location.pathname, location.search],
  )
  return [value, set]
}

/** "▶ Start Process": creates an execution for a schedule and opens it. */
export function useStartProcess() {
  const qc = useQueryClient()
  const toast = useToast()
  const navigate = useNavigate()
  return useMutation({
    mutationFn: (scheduleId: number) => api.post<ApiMessage>('/my-work/executions', { scheduleId }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['executions'] })
      qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
      qc.invalidateQueries({ queryKey: ['dashboard-departments'] })
      qc.invalidateQueries({ queryKey: ['my-work-processes'] })
      if (res.id) navigate(`/my-work/${res.id}`)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
}

// ---------------------------------------------------------------------------
// Small presentational pieces
// ---------------------------------------------------------------------------

export function ProgressBar({ done, total, className, size = 'sm', showLabel = true }: {
  done: number
  total: number
  className?: string
  size?: 'sm' | 'lg'
  showLabel?: boolean
}) {
  const pct = total === 0 ? 0 : Math.round((100 * done) / total)
  return (
    <div className={clsx('flex items-center gap-2 min-w-0', className)}>
      <div
        className={clsx('flex-1 rounded-full bg-muted overflow-hidden', size === 'lg' ? 'h-3' : 'h-2')}
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={pct}
        aria-label={`${done} of ${total} lines checked`}
      >
        <div className={clsx('h-full rounded-full transition-all', pct >= 100 ? 'bg-success' : 'bg-primary')} style={{ width: `${pct}%` }} />
      </div>
      {showLabel && (
        <span className={clsx('shrink-0 tabular-nums text-muted-foreground', size === 'lg' ? 'text-sm font-medium' : 'text-xs')}>
          {done}/{total}
        </span>
      )}
    </div>
  )
}

export function StatusBadge({ status }: { status: ExecutionStatusValue }) {
  if (status === ExecutionStatus.Completed) return <span className="badge bg-success/15 text-success">Completed</span>
  if (status === ExecutionStatus.Cancelled) return <span className="badge bg-destructive/10 text-destructive">Cancelled</span>
  return <span className="badge bg-primary/10 text-primary">In progress</span>
}

export function ColorDot({ color, className }: { color?: string | null; className?: string }) {
  return <span className={clsx('inline-block h-3 w-3 rounded-full ring-1 ring-black/10 shrink-0', className)} style={{ backgroundColor: color || '#94a3b8' }} />
}

// ---------------------------------------------------------------------------
// Tolerance helpers (mirror MyWorkController.IsOutOfRange)
// ---------------------------------------------------------------------------

export function parseNumber(s?: string | null): number | null {
  if (s == null || s.trim() === '') return null
  const cleaned = s.trim().replace(/[^0-9.-]/g, '')
  if (cleaned === '' || cleaned === '-' || cleaned === '.') return null
  const n = Number(cleaned)
  return Number.isFinite(n) ? n : null
}

export function isOutOfRange(value: string | null | undefined, min?: number | null, max?: number | null) {
  if (min == null && max == null) return false
  const v = parseNumber(value)
  if (v == null) return false
  return (min != null && v < min) || (max != null && v > max)
}

export function rangeText(min?: number | null, max?: number | null, unit?: string | null) {
  const u = unit && unit !== '-' ? ` ${unit}` : ''
  if (min != null && max != null) return `${min} – ${max}${u}`
  if (min != null) return `≥ ${min}${u}`
  if (max != null) return `≤ ${max}${u}`
  return ''
}
