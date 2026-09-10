import { useEffect, useState } from 'react'
import { format, parseISO } from 'date-fns'
import { CartesianGrid, Cell, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Activity, AlertTriangle, CheckCircle2, Cpu } from 'lucide-react'
import { Card, EmptyState, StatCard } from '@/components/ui'

// ---------------------------------------------------------------------------
// Types (backend: DashboardController.Summary)
// ---------------------------------------------------------------------------

export interface DashboardSummary {
  processesRunning: number
  completedLast7Days: number
  startedLast7Days: number
  defectsLast7Days: number
  devicesTotal: number
  devicesOnline: number
  schedulesCount: number
  executionsPerDay: { date: string; started: number; completed: number; cancelled: number }[]
  defectsByType: { defectTypeId: number; name: string; color?: string | null; quantity: number }[]
  topSchedules: { scheduleId: number; name: string; number: string; runs: number }[]
}

// ---------------------------------------------------------------------------
// Theme-aware chart tokens. Series colours are the first two slots of the validated categorical
// palette (blue, orange), stepped separately for light and dark surfaces.
// ---------------------------------------------------------------------------

/** Tracks the app theme (`data-theme` on <html>, toggled in the header). */
export function useIsDark() {
  const read = () => document.documentElement.dataset.theme === 'dark'
  const [dark, setDark] = useState(read)
  useEffect(() => {
    const obs = new MutationObserver(() => setDark(read()))
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    return () => obs.disconnect()
  }, [])
  return dark
}

export function chartTokens(dark: boolean) {
  return dark
    ? { series1: '#3987e5', series2: '#d95926', grid: '#2c2c2a', axis: '#898781', baseline: '#383835', surface: '#161b27', ink: '#ffffff', ink2: '#c3c2b7' }
    : { series1: '#2a78d6', series2: '#eb6834', grid: '#e1e0d9', axis: '#898781', baseline: '#c3c2b7', surface: '#ffffff', ink: '#0b0b0b', ink2: '#52514e' }
}

/** Fallback colours for defect types saved without a chart colour (fixed order, never cycled past 8). */
const FALLBACK = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948']

function tooltipStyle(t: ReturnType<typeof chartTokens>) {
  return {
    contentStyle: { background: t.surface, border: `1px solid ${t.grid}`, borderRadius: 8, fontSize: 12, color: t.ink, boxShadow: '0 4px 12px rgb(0 0 0 / 0.08)' },
    labelStyle: { color: t.ink2, fontWeight: 600, marginBottom: 4 },
    itemStyle: { color: t.ink, padding: 0 },
  }
}

// ---------------------------------------------------------------------------
// KPI row
// ---------------------------------------------------------------------------

export function KpiRow({ data, loading }: { data?: DashboardSummary; loading: boolean }) {
  const v = (n?: number) => (loading ? '…' : (n ?? 0).toLocaleString())
  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
      <StatCard label="Processes running" value={v(data?.processesRunning)} tone="primary" icon={<Activity className="h-4 w-4" />} hint={data && `${data.startedLast7Days} started in the last 7 days`} />
      <StatCard label="Completed (7 days)" value={v(data?.completedLast7Days)} tone="success" icon={<CheckCircle2 className="h-4 w-4" />} hint={data && `${data.schedulesCount} process schedule${data.schedulesCount === 1 ? '' : 's'}`} />
      <StatCard label="Defects (7 days)" value={v(data?.defectsLast7Days)} tone={data && data.defectsLast7Days > 0 ? 'danger' : 'default'} icon={<AlertTriangle className="h-4 w-4" />} hint="Sum of recorded quantities" />
      <StatCard
        label="Devices online"
        value={loading ? '…' : `${data?.devicesOnline ?? 0}/${data?.devicesTotal ?? 0}`}
        icon={<Cpu className="h-4 w-4" />}
        hint="Reported in the last 10 minutes"
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Processes started vs completed (14 days)
// ---------------------------------------------------------------------------

export function ExecutionsChart({ data }: { data?: DashboardSummary }) {
  const t = chartTokens(useIsDark())
  const rows = (data?.executionsPerDay ?? []).map((d) => ({ ...d, label: format(parseISO(d.date), 'M/d') }))
  const totalStarted = rows.reduce((s, r) => s + r.started, 0)
  const totalCompleted = rows.reduce((s, r) => s + r.completed, 0)
  const empty = rows.length > 0 && totalStarted === 0 && totalCompleted === 0

  return (
    <Card title="Processes started vs completed (14 days)" className="xl:col-span-2 min-w-0">
      <div className="flex flex-wrap gap-4 mb-2 text-xs" aria-label="Legend">
        <LegendItem color={t.series1} label="Started" value={totalStarted} />
        <LegendItem color={t.series2} label="Completed" value={totalCompleted} />
      </div>
      {empty ? (
        <EmptyState title="No processes in the last 14 days" description="Start a process from My Work to see activity here." />
      ) : (
        <div className="h-64" role="img" aria-label={`Line chart: ${totalStarted} processes started and ${totalCompleted} completed in the last 14 days`}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={rows} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
              <CartesianGrid vertical={false} stroke={t.grid} />
              <XAxis dataKey="label" tick={{ fontSize: 11, fill: t.axis }} tickLine={false} axisLine={{ stroke: t.baseline }} interval="preserveStartEnd" minTickGap={16} />
              <YAxis allowDecimals={false} tick={{ fontSize: 11, fill: t.axis }} tickLine={false} axisLine={false} width={40} />
              <Tooltip {...tooltipStyle(t)} cursor={{ stroke: t.baseline, strokeWidth: 1 }} />
              <Line type="monotone" dataKey="started" name="Started" stroke={t.series1} strokeWidth={2} dot={false} activeDot={{ r: 5, strokeWidth: 2, stroke: t.surface }} />
              <Line type="monotone" dataKey="completed" name="Completed" stroke={t.series2} strokeWidth={2} dot={false} activeDot={{ r: 5, strokeWidth: 2, stroke: t.surface }} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </Card>
  )
}

function LegendItem({ color, label, value }: { color: string; label: string; value: number }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-muted-foreground">
      <span className="h-0.5 w-4 rounded-full" style={{ backgroundColor: color, height: 3 }} />
      {label}
      <span className="font-semibold text-foreground tabular-nums">{value}</span>
    </span>
  )
}

// ---------------------------------------------------------------------------
// Defects by type (30 days) — colours come from each defect type's chart colour
// ---------------------------------------------------------------------------

export function DefectsDonut({ data }: { data?: DashboardSummary }) {
  const t = chartTokens(useIsDark())
  const rows = (data?.defectsByType ?? []).map((d, i) => ({ ...d, fill: d.color || FALLBACK[i % FALLBACK.length] }))
  const total = rows.reduce((s, r) => s + r.quantity, 0)

  return (
    <Card title="Defects by type (30 days)" className="min-w-0">
      {total === 0 ? (
        <EmptyState title="No defects recorded" description="Defects recorded on processes in the last 30 days appear here." />
      ) : (
        <div className="flex flex-col sm:flex-row xl:flex-col 2xl:flex-row items-center gap-4">
          <div className="relative h-48 w-48 shrink-0" role="img" aria-label={`Donut chart of ${total} defects by type`}>
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={rows} dataKey="quantity" nameKey="name" innerRadius={58} outerRadius={86} paddingAngle={rows.length > 1 ? 2 : 0} stroke={t.surface} strokeWidth={2} isAnimationActive={false}>
                  {rows.map((r) => (
                    <Cell key={r.defectTypeId} fill={r.fill} />
                  ))}
                </Pie>
                <Tooltip {...tooltipStyle(t)} />
              </PieChart>
            </ResponsiveContainer>
            <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
              <div className="text-2xl font-semibold tabular-nums">{total}</div>
              <div className="text-[11px] uppercase tracking-wide text-muted-foreground">defects</div>
            </div>
          </div>
          <ul className="w-full space-y-1.5 text-sm">
            {rows.map((r) => (
              <li key={r.defectTypeId} className="flex items-center gap-2">
                <span className="h-3 w-3 rounded-sm shrink-0" style={{ backgroundColor: r.fill }} />
                <span className="flex-1 truncate">{r.name}</span>
                <span className="tabular-nums font-semibold">{r.quantity}</span>
                <span className="w-10 text-right tabular-nums text-xs text-muted-foreground">{Math.round((100 * r.quantity) / total)}%</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  )
}

// ---------------------------------------------------------------------------
// Top process schedules by runs (30 days)
// ---------------------------------------------------------------------------

export function TopSchedules({ data }: { data?: DashboardSummary }) {
  const t = chartTokens(useIsDark())
  const rows = data?.topSchedules ?? []
  const max = Math.max(1, ...rows.map((r) => r.runs))
  return (
    <Card title="Most run processes (30 days)" className="min-w-0">
      {rows.length === 0 ? (
        <EmptyState title="No runs yet" />
      ) : (
        <ul className="space-y-3">
          {rows.map((r) => (
            <li key={r.scheduleId}>
              <div className="flex items-baseline justify-between gap-2 text-sm">
                <span className="truncate">
                  {r.name} <span className="text-xs text-muted-foreground">#{r.number}</span>
                </span>
                <span className="tabular-nums font-semibold">{r.runs}</span>
              </div>
              <div className="mt-1 h-2 rounded-full bg-muted overflow-hidden">
                <div className="h-full rounded-full" style={{ width: `${(100 * r.runs) / max}%`, backgroundColor: t.series1 }} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
