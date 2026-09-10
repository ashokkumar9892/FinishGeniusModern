import { useEffect, useMemo, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { DollarSign, FileText, Printer, Save } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime, money, num, toNumber } from '@/lib/format'
import { useToast } from '@/components/toast'
import { Card, EmptyState, ErrorBanner, Field, LoadingBlock, PageHeader, Spinner } from '@/components/ui'
import { EntityDocuments } from '@/components/EntityDocuments'
import { logoSrc } from '@/components/Layout'
import { BigStat, ScheduleFilter, scheduleLabel, useDebounced, useScheduleParam } from './shared'
import type { Estimates, PricingInput, PricingResult } from './types'

const EMPTY_TEXT = 'Please select a Process System using the filters above.'

type FormKey = keyof PricingInput
const blank: Record<FormKey, string> = {
  laborRate: '0', markUp: '0', premiumMarkUp: '0',
  oneSidedComplexity: '0', oneSidedArea: '0', twoSidedComplexity: '0', twoSidedArea: '0', highComplexity: '0', highComplexityArea: '0',
}

const perSqFt = (n: number) => `$${Number(n ?? 0).toLocaleString('en-US', { minimumFractionDigits: 4, maximumFractionDigits: 4 })}`

/** Theme token as a concrete colour (SVG presentation attributes cannot resolve CSS variables). */
function token(name: string, fallback: string) {
  if (typeof window === 'undefined') return fallback
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return v ? `hsl(${v})` : fallback
}

function NumInput({ value, onChange, suffix, prefix, label }: { value: string; onChange: (v: string) => void; suffix?: string; prefix?: string; label: string }) {
  return (
    <div className="relative">
      {prefix && <span className="absolute left-3 top-1/2 -translate-y-1/2 text-xs text-muted-foreground pointer-events-none">{prefix}</span>}
      <input
        className={`input tabular-nums ${prefix ? 'pl-6' : ''} ${suffix ? 'pr-12' : ''}`}
        inputMode="decimal"
        aria-label={label}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onFocus={(e) => e.target.select()}
      />
      {suffix && <span className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-muted-foreground pointer-events-none">{suffix}</span>}
    </div>
  )
}

export default function PricingPage() {
  const { groupId } = useGroup()
  const [scheduleId, setScheduleId] = useScheduleParam()
  const [docsOpen, setDocsOpen] = useState(false)
  const [form, setForm] = useState<Record<FormKey, string>>(blank)
  const toast = useToast()
  const qc = useQueryClient()

  const est = useQuery({
    queryKey: ['process-schedule-estimates', scheduleId],
    queryFn: () => api.get<Estimates>(`/process-schedules/${scheduleId}/estimates`).then((r) => r.data),
    enabled: !!scheduleId,
  })

  useEffect(() => {
    const e = est.data
    if (!e) return
    const s = (n: number) => String(Number(n ?? 0))
    setForm({
      laborRate: s(e.laborRate), markUp: s(e.markUp), premiumMarkUp: s(e.premiumMarkUp),
      oneSidedComplexity: s(e.oneSidedComplexity), oneSidedArea: s(e.oneSidedPriceArea),
      twoSidedComplexity: s(e.twoSidedComplexity), twoSidedArea: s(e.twoSidedPriceArea),
      highComplexity: s(e.highComplexity), highComplexityArea: s(e.highComplexityArea),
    })
  }, [est.data])

  // "020" → 20, "" → 0, negatives are not allowed.
  const input = useMemo(() => {
    const o = {} as PricingInput
    ;(Object.keys(blank) as FormKey[]).forEach((k) => (o[k] = Math.max(0, toNumber(form[k]))))
    return o
  }, [form])
  const debounced = useDebounced(input, 300)

  const price = useQuery({
    queryKey: ['process-schedule-pricing', scheduleId, debounced],
    queryFn: () => api.post<PricingResult>(`/process-schedules/${scheduleId}/pricing`, debounced).then((r) => r.data),
    enabled: !!scheduleId,
    placeholderData: keepPreviousData,
  })

  const e = est.data
  const saved =
    !!e &&
    input.laborRate === Number(e.laborRate) && input.markUp === Number(e.markUp) && input.premiumMarkUp === Number(e.premiumMarkUp) &&
    input.oneSidedComplexity === Number(e.oneSidedComplexity) && input.oneSidedArea === Number(e.oneSidedPriceArea) &&
    input.twoSidedComplexity === Number(e.twoSidedComplexity) && input.twoSidedArea === Number(e.twoSidedPriceArea) &&
    input.highComplexity === Number(e.highComplexity) && input.highComplexityArea === Number(e.highComplexityArea)

  const save = useMutation({
    mutationFn: () => api.put(`/process-schedules/${scheduleId}/pricing`, input),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['process-schedule-estimates', scheduleId] })
    },
    onError: (err) => toast.error(errorMessage(err)),
  })

  const set = (k: FormKey) => (v: string) => setForm((f) => ({ ...f, [k]: v }))
  const r = price.data
  const calculating = price.isFetching || JSON.stringify(input) !== JSON.stringify(debounced)
  const totalArea = input.oneSidedArea + input.twoSidedArea + input.highComplexityArea

  const rows: { label: string; c: FormKey; a: FormKey; hint: string }[] = [
    { label: 'One Sided Finishing', c: 'oneSidedComplexity', a: 'oneSidedArea', hint: '1 side · Mark-Up' },
    { label: 'Two Sided Finishing', c: 'twoSidedComplexity', a: 'twoSidedArea', hint: '2 sides · Mark-Up' },
    { label: 'High Complexity', c: 'highComplexity', a: 'highComplexityArea', hint: '1 side · Mark-Up + Premium' },
  ]

  const primary = token('--primary', '#e8590c')
  const muted = token('--muted-foreground', '#64748b')
  const grid = token('--border', '#e2e8f0')
  const chartData = (r?.rows ?? []).map((x) => ({ name: x.label, cost: Number(x.cost.toFixed(2)) }))

  return (
    <>
      <PageHeader
        title="Process System Pricing"
        breadcrumbs={['Processes', 'Process System Pricing']}
        actions={
          <>
            <button className="btn-secondary" onClick={() => setDocsOpen(true)}>
              <FileText className="h-4 w-4" /> + Documents
            </button>
            <button className="btn-secondary" disabled={!r} onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print
            </button>
            <button className="btn-primary" disabled={!scheduleId || !e || e.isArchived || save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        }
      />
      <div className="mb-4">
        <ScheduleFilter value={scheduleId} onChange={setScheduleId} />
      </div>

      {!scheduleId ? (
        <div className="card">
          <EmptyState icon={<DollarSign className="h-5 w-5" />} title={EMPTY_TEXT} />
        </div>
      ) : est.isError ? (
        <ErrorBanner message={errorMessage(est.error)} />
      ) : !e ? (
        <LoadingBlock />
      ) : (
        <>
          <div className="print-only mb-4 border-b-2 border-orange-500 pb-2">
            <div className="flex items-center gap-2">
              <img src={logoSrc} alt="Finish Genius PRO" className="h-8" />
              <div className="text-[10px] uppercase tracking-[0.2em] text-orange-600 font-semibold">Finish Genius · Process System Pricing</div>
            </div>
            <div className="text-lg font-bold">{scheduleLabel(e)}</div>
            <div className="text-xs">
              {e.customerName && <>Customer: {e.customerName} · </>}Labor {money(input.laborRate)}/hr · Mark-Up {num(input.markUp)}% · Premium Mark-Up{' '}
              {num(input.premiumMarkUp)}% · Printed {dateTime(new Date().toISOString())}
            </div>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)] mb-4">
            <Card
              title="Assumptions"
              actions={saved ? <span className="badge bg-emerald-100 text-emerald-800">Saved</span> : <span className="badge bg-amber-100 text-amber-800">Not saved</span>}
            >
              <div className="grid gap-4">
                <Field label="Labor Rate ($/hr)">
                  <NumInput label="Labor Rate" prefix="$" suffix="/ hr" value={form.laborRate} onChange={set('laborRate')} />
                </Field>
                <Field label="Mark-Up (%)">
                  <NumInput label="Mark-Up" suffix="%" value={form.markUp} onChange={set('markUp')} />
                </Field>
                <Field label="Premium Mark-Up (%)" hint="Applied on top of Mark-Up to the High Complexity area.">
                  <NumInput label="Premium Mark-Up" suffix="%" value={form.premiumMarkUp} onChange={set('premiumMarkUp')} />
                </Field>
              </div>
            </Card>
            <Card title="Complexity / Surface Area">
              <div className="hidden sm:grid grid-cols-[minmax(0,1fr)_7.5rem_9rem] gap-3 pb-2 text-xs font-semibold text-muted-foreground">
                <span />
                <span>Complexity (%)</span>
                <span>Surface Area (sq ft)</span>
              </div>
              <div className="space-y-3">
                {rows.map((row) => (
                  <div key={row.label} className="grid grid-cols-2 sm:grid-cols-[minmax(0,1fr)_7.5rem_9rem] gap-3 items-center">
                    <div className="col-span-2 sm:col-span-1">
                      <div className="text-sm font-medium">{row.label}</div>
                      <div className="text-xs text-muted-foreground">{row.hint}</div>
                    </div>
                    <NumInput label={`${row.label} complexity`} suffix="%" value={form[row.c]} onChange={set(row.c)} />
                    <NumInput label={`${row.label} surface area`} suffix="sq ft" value={form[row.a]} onChange={set(row.a)} />
                  </div>
                ))}
              </div>
            </Card>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 mb-4">
            <BigStat label="Total Price" value={money(r?.totalPrice ?? 0)} hint={calculating ? 'Calculating…' : e.totalJobPrice ? `Saved: ${money(e.totalJobPrice)}` : undefined} />
            <BigStat label="Total Square Footage" value={num(r?.totalSquareFootage ?? totalArea, 2)} hint="Sum of the three surface areas" />
          </div>
          {price.isError && <ErrorBanner message={errorMessage(price.error)} />}

          {r && (
            <div className="grid gap-4 lg:grid-cols-2">
              <Card title="Pricing assumptions" bodyClassName="p-0">
                <table className="w-full text-sm">
                  <tbody>
                    <BreakRow label="Material cost" sub="Σ gallons per sq ft × price" value={perSqFt(r.materialCostPerSqFt)} unit="/ sq ft" />
                    <BreakRow label="Labor cost" sub={`hours per sq ft × ${money(input.laborRate)}/hr`} value={perSqFt(r.laborCostPerSqFt)} unit="/ sq ft" />
                    <BreakRow label="Other cost" sub="Process costing ($ / sq ft values)" value={perSqFt(r.otherCostPerSqFt)} unit="/ sq ft" />
                    <BreakRow label="Base cost" value={perSqFt(r.baseCostPerSqFt)} unit="/ sq ft" strong />
                    {r.rows.map((x) => (
                      <BreakRow
                        key={x.label}
                        label={x.label}
                        sub={`${num(x.area)} sq ft × ${x.sides} side${x.sides === 1 ? '' : 's'} × (1 + ${num(x.complexityPercent)}%)`}
                        value={money(x.cost)}
                      />
                    ))}
                    <BreakRow label="Subtotal" value={money(r.subtotal)} strong />
                    <BreakRow label={`Mark-Up (${num(input.markUp)}%)`} value={money(r.markUpAmount)} />
                    <BreakRow label={`Premium Mark-Up (${num(input.premiumMarkUp)}%)`} sub="High Complexity row only" value={money(r.premiumMarkUpAmount)} />
                    <BreakRow label="Total Price" value={money(r.totalPrice)} strong />
                  </tbody>
                </table>
              </Card>
              <Card title="Cost by row (before mark-up)" className="no-print">
                {r.subtotal === 0 ? (
                  <EmptyState title="No cost yet" description="Enter surface areas (and a labor rate) to see how the price builds up." />
                ) : (
                  <div className="h-56">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={chartData} layout="vertical" margin={{ top: 4, right: 16, bottom: 4, left: 8 }} barCategoryGap="30%">
                        <CartesianGrid horizontal={false} stroke={grid} strokeDasharray="3 3" />
                        <XAxis type="number" tick={{ fill: muted, fontSize: 11 }} tickFormatter={(v: number) => `$${num(v)}`} axisLine={false} tickLine={false} />
                        <YAxis type="category" dataKey="name" width={130} tick={{ fill: muted, fontSize: 12 }} axisLine={false} tickLine={false} />
                        <Tooltip
                          cursor={{ fill: grid, opacity: 0.4 }}
                          formatter={(v) => [money(Number(v)), 'Cost']}
                          contentStyle={{ borderRadius: 8, fontSize: 12 }}
                        />
                        <Bar dataKey="cost" fill={primary} radius={[0, 4, 4, 0]} maxBarSize={28} />
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                )}
              </Card>
            </div>
          )}
        </>
      )}

      {docsOpen && <EntityDocuments open onClose={() => setDocsOpen(false)} entityType="Pricing" entityId={0} groupId={groupId} title="Pricing Documents" />}
    </>
  )
}

function BreakRow({ label, sub, value, unit, strong }: { label: string; sub?: string; value: string; unit?: string; strong?: boolean }) {
  return (
    <tr className={strong ? 'border-y bg-muted/40 font-semibold' : 'border-b last:border-0'}>
      <td className="td">
        {label}
        {sub && <div className="text-xs font-normal text-muted-foreground">{sub}</div>}
      </td>
      <td className="td text-right tabular-nums whitespace-nowrap">
        {value}
        {unit && <span className="ml-1 text-xs font-normal text-muted-foreground">{unit}</span>}
      </td>
    </tr>
  )
}
