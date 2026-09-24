import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, FileSpreadsheet, LineChart as LineChartIcon } from 'lucide-react'
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import clsx from 'clsx'
import { api, download, errorMessage } from '@/lib/api'
import { dateTime, num } from '@/lib/format'
import { Card, EmptyState, ErrorBanner, Field, LoadingBlock, Note, PageHeader, StatCard, Tabs } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { useToast } from '@/components/toast'

interface Entry {
  runId: number
  at: string
  user?: string | null
  step: number
  stepName: string
  description: string
  value?: string | null
  unit?: string | null
  min?: number | null
  max?: number | null
  inRange?: boolean | null
}

interface DefectRow { runId: number; at: string; user?: string | null; type: string; quantity: number; notes?: string | null }
interface AdderRow { runId: number; at: string; user?: string | null; type: string; value: string }

interface ProcessData {
  schedule: { id: number; name: string; number?: string | null; customerName?: string | null; groupId: number }
  range: { runsShown: number; cappedAt: number; asked: number }
  totals: {
    runs: number; completed: number; inProgress: number; entries: number; measurements: number
    checked: number; passed: number; failed: number; defects: number; adders: number
  }
  runsPerDay: { date: string; runs: number; completed: number }[]
  series: { name: string; unit?: string | null; min?: number | null; max?: number | null; points: { at: string; value: number; runId: number; user?: string | null }[] }[]
  defectsByType: { name: string; quantity: number }[]
  entries: Entry[]
  defects: DefectRow[]
  adders: AdderRow[]
}

const monthAgo = () => {
  const d = new Date()
  d.setMonth(d.getMonth() - 1)
  return d.toISOString().slice(0, 10)
}

/**
 * Everything recorded against one process schedule: the measured values as charts, and the checklist entries, defects
 * and adders as tables. The old site split this across "Graphs", the quality-control charts and "Process Data Details";
 * they are the same runs looked at three ways, so they are one screen here.
 */
export default function ProcessDataPage() {
  const { id } = useParams()
  const scheduleId = Number(id)
  const [params] = useSearchParams()
  const toast = useToast()
  const [from, setFrom] = useState(params.get('from') ?? monthAgo)
  const [to, setTo] = useState(params.get('to') ?? (() => new Date().toISOString().slice(0, 10)))
  const [runs, setRuns] = useState(15)
  const [tab, setTab] = useState<'entries' | 'defects' | 'adders'>('entries')
  const [busy, setBusy] = useState(false)

  const q = useQuery({
    queryKey: ['process-data', scheduleId, from, to, runs],
    queryFn: () => api.get<ProcessData>(`/process-data/${scheduleId}`, { params: { from, to, runs } }).then((r) => r.data),
    enabled: scheduleId > 0,
  })
  const d = q.data

  const entryColumns: Column<Entry>[] = [
    { key: 'at', header: 'When', cell: (r) => dateTime(r.at), sortValue: (r) => r.at },
    { key: 'user', header: 'Operator', cell: (r) => r.user || '—' },
    { key: 'step', header: 'Step', cell: (r) => `${r.step}. ${r.stepName}`, hideBelow: 'lg' },
    { key: 'description', header: 'Description' },
    {
      key: 'value',
      header: 'Value',
      align: 'right',
      cell: (r) => (
        <span className={clsx('tabular-nums', r.inRange === false && 'font-semibold text-destructive')}>
          {r.value ?? '—'} {r.unit}
        </span>
      ),
    },
    {
      key: 'limits',
      header: 'Limits',
      align: 'right',
      hideBelow: 'xl',
      cell: (r) => (r.min == null && r.max == null ? '—' : `${r.min ?? ''}–${r.max ?? ''}`),
    },
    {
      key: 'inRange',
      header: 'Result',
      cell: (r) =>
        r.inRange == null ? (
          <span className="text-muted-foreground">recorded</span>
        ) : r.inRange ? (
          <span className="badge bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-300">In range</span>
        ) : (
          <span className="badge bg-rose-100 text-rose-900 dark:bg-rose-500/15 dark:text-rose-200">Out of range</span>
        ),
    },
    { key: 'runId', header: 'Run', align: 'right', hideBelow: 'lg', cell: (r) => <Link className="text-primary hover:underline" to={`/my-work/${r.runId}`}>#{r.runId}</Link> },
  ]

  const passRate = useMemo(() => {
    if (!d?.totals.checked) return null
    return Math.round((d.totals.passed / d.totals.checked) * 100)
  }, [d])

  async function toExcel() {
    setBusy(true)
    try {
      await download(`/process-data/${scheduleId}/excel`, 'ProcessData.xlsx', { from, to, runs })
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeader
        title={d ? `Process data: ${d.schedule.name}` : 'Process data'}
        breadcrumbs={[['Process Schedules', '/process-schedules'], 'Process data']}
        subtitle={d?.schedule.number ? `Schedule ${d.schedule.number}${d.schedule.customerName ? ` · ${d.schedule.customerName}` : ''}` : undefined}
        actions={
          <>
            <Link className="btn-secondary" to="/process-schedules">
              <ArrowLeft className="h-4 w-4" /> Back
            </Link>
            <button className="btn-primary" disabled={busy || !d} onClick={() => void toExcel()}>
              <FileSpreadsheet className="h-4 w-4" /> Export
            </button>
          </>
        }
      />

      <Card className="mb-4">
        <div className="grid gap-3 sm:grid-cols-[repeat(3,minmax(0,180px))_1fr] sm:items-end">
          <Field label="From">
            <input className="input" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label="To">
            <input className="input" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </Field>
          <Field label="Runs to open" hint="Each run is read separately; more runs means a longer wait.">
            <select className="input" value={runs} onChange={(e) => setRuns(Number(e.target.value))}>
              {[5, 15, 25, 40].map((n) => <option key={n} value={n}>{n} most recent</option>)}
            </select>
          </Field>
          {d && (
            <div className="text-xs text-muted-foreground sm:pb-2">
              Showing the {d.range.runsShown} most recent run{d.range.runsShown === 1 ? '' : 's'} in this period
              {d.range.runsShown >= d.range.asked && ' — there may be older ones; raise "runs to open" or narrow the dates'}.
            </div>
          )}
        </div>
      </Card>

      <ErrorBanner message={q.isError ? errorMessage(q.error) : null} />
      {q.isLoading && <LoadingBlock label="Reading the runs…" />}

      {d && (
        <>
          <div className="mb-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <StatCard label="Runs" value={d.totals.runs} hint={`${d.totals.completed} completed · ${d.totals.inProgress} running`} />
            <StatCard label="Entries recorded" value={d.totals.entries} hint={`${d.totals.measurements} with a number`} />
            <StatCard
              label="Within limits"
              value={passRate == null ? '—' : `${passRate}%`}
              tone={passRate == null ? 'default' : passRate >= 95 ? 'success' : passRate >= 80 ? 'default' : 'danger'}
              hint={d.totals.checked ? `${d.totals.passed} in range · ${d.totals.failed} out` : 'No entry had limits set'}
            />
            <StatCard label="Defects" value={d.totals.defects} tone={d.totals.defects > 0 ? 'danger' : 'default'} hint={`${d.defectsByType.length} kind${d.defectsByType.length === 1 ? '' : 's'}`} />
            <StatCard label="Adders" value={d.totals.adders} />
          </div>

          {d.series.length === 0 ? (
            <Card className="mb-4">
              <EmptyState
                icon={<LineChartIcon className="h-5 w-5" />}
                title="Nothing measured to chart yet"
                description="A line is drawn for every checklist entry that records a number, once there are at least two of them in the period."
              />
            </Card>
          ) : (
            <div className="mb-4 grid gap-4 2xl:grid-cols-2">
              {d.series.map((s) => (
                <Card key={s.name} title={<span className="text-sm">{s.name}{s.unit ? ` (${s.unit})` : ''}</span>}>
                  <div className="h-56">
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={s.points.map((p) => ({ ...p, label: dateTime(p.at) }))} margin={{ top: 6, right: 10, bottom: 0, left: -10 }}>
                        <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
                        <XAxis dataKey="label" tick={{ fontSize: 10 }} minTickGap={24} />
                        <YAxis tick={{ fontSize: 10 }} domain={['auto', 'auto']} />
                        <Tooltip
                          contentStyle={{ fontSize: 12 }}
                          formatter={(v) => [`${num(Number(v), 4)}${s.unit ? ` ${s.unit}` : ''}`, s.name]}
                          labelFormatter={(l) => String(l)}
                        />
                        {s.min != null && <ReferenceLine y={s.min} stroke="#e11d48" strokeDasharray="4 4" label={{ value: `min ${s.min}`, fontSize: 10 }} />}
                        {s.max != null && <ReferenceLine y={s.max} stroke="#e11d48" strokeDasharray="4 4" label={{ value: `max ${s.max}`, fontSize: 10 }} />}
                        <Line type="monotone" dataKey="value" stroke="#f97316" strokeWidth={2} dot={s.points.length <= 40} isAnimationActive={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {d.defectsByType.length > 0 && (
            <Card className="mb-4" title="Defects by kind">
              <ul className="space-y-1.5">
                {d.defectsByType.map((x) => {
                  const worst = d.defectsByType[0].quantity || 1
                  return (
                    <li key={x.name} className="flex items-center gap-3 text-sm">
                      <span className="w-48 shrink-0 truncate">{x.name}</span>
                      <span className="h-3 rounded bg-destructive/70" style={{ width: `${Math.max(4, (x.quantity / worst) * 60)}%` }} />
                      <span className="tabular-nums text-muted-foreground">{x.quantity}</span>
                    </li>
                  )
                })}
              </ul>
            </Card>
          )}

          <Tabs
            className="mb-4"
            value={tab}
            onChange={setTab}
            tabs={[
              { key: 'entries', label: 'Checklist entries', count: d.entries.length },
              { key: 'defects', label: 'Defects', count: d.defects.length },
              { key: 'adders', label: 'Adders', count: d.adders.length },
            ]}
          />

          {tab === 'entries' && (
            <DataTable
              rows={d.entries}
              columns={entryColumns}
              rowKey={(r) => `${r.runId}-${r.at}-${r.description}`}
              searchPlaceholder="Search entries…"
              searchText={(r) => [r.user, r.stepName, r.description, r.value].filter(Boolean).join(' ')}
              emptyTitle="Nothing was recorded in this period"
            />
          )}
          {tab === 'defects' && (
            <DataTable
              rows={d.defects}
              columns={[
                { key: 'at', header: 'When', cell: (r) => dateTime(r.at) },
                { key: 'type', header: 'Defect' },
                { key: 'quantity', header: 'Quantity', align: 'right' },
                { key: 'notes', header: 'Notes', cell: (r) => r.notes || '—' },
                { key: 'runId', header: 'Run', align: 'right', cell: (r) => <Link className="text-primary hover:underline" to={`/my-work/${r.runId}`}>#{r.runId}</Link> },
              ]}
              rowKey={(r) => `${r.runId}-${r.at}-${r.type}`}
              emptyTitle="No defects recorded in this period"
            />
          )}
          {tab === 'adders' && (
            <DataTable
              rows={d.adders}
              columns={[
                { key: 'at', header: 'When', cell: (r) => dateTime(r.at) },
                { key: 'type', header: 'Adder' },
                { key: 'value', header: 'Value' },
                { key: 'runId', header: 'Run', align: 'right', cell: (r) => <Link className="text-primary hover:underline" to={`/my-work/${r.runId}`}>#{r.runId}</Link> },
              ]}
              rowKey={(r) => `${r.runId}-${r.at}-${r.type}`}
              emptyTitle="No adders recorded in this period"
            />
          )}

          <Note tone="info">
            Values come from what operators actually recorded on the shop floor. A dashed line on a chart is the limit set
            for that entry in the schedule; anything outside it is red in the table below.
          </Note>
        </>
      )}
    </>
  )
}
