import { useEffect, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Calculator, FileText, Printer, Save } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime, money, num, toNumber } from '@/lib/format'
import { useToast } from '@/components/toast'
import { Card, EmptyState, ErrorBanner, Field, LoadingBlock, Note, PageHeader, Spinner } from '@/components/ui'
import { EntityDocuments } from '@/components/EntityDocuments'
import { logoSrc } from '@/components/Layout'
import { BigStat, ScheduleFilter, scheduleLabel, useDebounced, useScheduleParam } from './shared'
import type { Estimates, QuantityResult } from './types'

const EMPTY_TEXT = 'Please select a Process System using the filters above.'
const fixed = (n: number, d: number) => Number(n ?? 0).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d })

export default function MaterialQuantitiesPage() {
  const { groupId } = useGroup()
  const [scheduleId, setScheduleId] = useScheduleParam()
  const [docsOpen, setDocsOpen] = useState(false)
  const toast = useToast()
  const qc = useQueryClient()

  const [one, setOne] = useState('0')
  const [two, setTwo] = useState('0')

  const est = useQuery({
    queryKey: ['process-schedule-estimates', scheduleId],
    queryFn: () => api.get<Estimates>(`/process-schedules/${scheduleId}/estimates`).then((r) => r.data),
    enabled: !!scheduleId,
  })

  // Prefill from the saved estimate whenever the schedule changes.
  useEffect(() => {
    if (!est.data) return
    setOne(String(Number(est.data.oneSidedArea)))
    setTwo(String(Number(est.data.twoSidedArea)))
  }, [est.data])

  const oneN = Math.max(0, toNumber(one))
  const twoN = Math.max(0, toNumber(two))
  const params = useDebounced({ one: oneN, two: twoN }, 300)

  const qty = useQuery({
    queryKey: ['process-schedule-quantities', scheduleId, params.one, params.two],
    queryFn: () => api.get<QuantityResult>(`/process-schedules/${scheduleId}/quantities`, { params: { oneSided: params.one, twoSided: params.two } }).then((r) => r.data),
    enabled: !!scheduleId,
    placeholderData: keepPreviousData,
  })

  const saved = est.data && Number(est.data.oneSidedArea) === oneN && Number(est.data.twoSidedArea) === twoN
  const save = useMutation({
    mutationFn: () => api.put(`/process-schedules/${scheduleId}/quantities`, { oneSidedArea: oneN, twoSidedArea: twoN }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['process-schedule-estimates', scheduleId] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const r = qty.data
  const calculating = qty.isFetching || params.one !== oneN || params.two !== twoN

  return (
    <>
      <PageHeader
        title="Material Quantities"
        breadcrumbs={['Processes', 'Material Quantities']}
        actions={
          <>
            <button className="btn-secondary" onClick={() => setDocsOpen(true)}>
              <FileText className="h-4 w-4" /> + Documents
            </button>
            <button className="btn-secondary" disabled={!r} onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print
            </button>
            <button className="btn-primary" disabled={!scheduleId || !est.data || est.data.isArchived || save.isPending} onClick={() => save.mutate()}>
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
          <EmptyState icon={<Calculator className="h-5 w-5" />} title={EMPTY_TEXT} />
        </div>
      ) : est.isError ? (
        <ErrorBanner message={errorMessage(est.error)} />
      ) : !est.data ? (
        <LoadingBlock />
      ) : (
        <>
          {/* Print header */}
          <div className="print-only mb-4 border-b-2 border-orange-500 pb-2">
            <div className="flex items-center gap-2">
              <img src={logoSrc} alt="Finish Genius PRO" className="h-8" />
              <div className="text-[10px] uppercase tracking-[0.2em] text-orange-600 font-semibold">Finish Genius · Material Quantities</div>
            </div>
            <div className="text-lg font-bold">{scheduleLabel(est.data)}</div>
            <div className="text-xs">
              {est.data.customerName && <>Customer: {est.data.customerName} · </>}One sided {num(oneN)} sq ft · Two sided {num(twoN)} sq ft · Printed{' '}
              {dateTime(new Date().toISOString())}
            </div>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] mb-4">
            <Card title="Surface Area" className="no-print" actions={!saved ? <span className="badge bg-amber-100 text-amber-800">Not saved</span> : <span className="badge bg-emerald-100 text-emerald-800">Saved</span>}>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="One Sided Finishing (sq ft)">
                  <input className="input" inputMode="decimal" value={one} onChange={(e) => setOne(e.target.value)} onFocus={(e) => e.target.select()} />
                </Field>
                <Field label="Two Sided Finishing (sq ft)" hint="Counted twice (both faces).">
                  <input className="input" inputMode="decimal" value={two} onChange={(e) => setTwo(e.target.value)} onFocus={(e) => e.target.select()} />
                </Field>
              </div>
              {est.data.isArchived && <p className="mt-3 text-xs text-muted-foreground">This schedule is archived; the estimate can be viewed but not saved.</p>}
            </Card>
            <div className="grid gap-4 sm:grid-cols-2">
              <BigStat label="Square Footage" value={fixed(r?.squareFootage ?? oneN + 2 * twoN, 2)} hint="One sided + 2 × two sided" />
              <BigStat
                label="Production Time (Hrs)"
                value={fixed(r?.productionHours ?? 0, 2)}
                hint={r?.hasProductionRate ? `${num((r?.hoursPerSqFt ?? 0) * 60, 4)} min per sq ft` : 'No production rate in this schedule'}
              />
            </div>
          </div>

          {r && !r.hasCoverage && (
            <div className="mb-4 no-print">
              <Note tone="info">
                No material quantities can be calculated for this schedule yet. Quantities come from the sub step values of its process steps:
                a characteristic tagged <b>Coverage (sq ft / gal)</b> in the same sub step as the materials, characteristics tagged
                <b> Material used (quantities)</b> for each material, and optional <b>Mix % of previous material</b> values. Production time uses
                <b> Production rate (sq ft / hr)</b>. Set these calculation variables in Material Categories › Characteristics, then fill the values in
                the process step builder.
              </Note>
            </div>
          )}
          {qty.isError && <ErrorBanner message={errorMessage(qty.error)} />}

          <Card
            title="Material Quantities"
            bodyClassName="p-0"
            actions={calculating ? <span className="flex items-center gap-1 text-xs text-muted-foreground no-print"><Spinner className="h-3 w-3" /> Calculating…</span> : undefined}
          >
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted/60 border-b">
                  <tr>
                    <th className="th">Name</th>
                    <th className="th text-right">Quantity</th>
                    <th className="th text-right hidden sm:table-cell">Price / unit</th>
                    <th className="th text-right">Est. cost</th>
                  </tr>
                </thead>
                <tbody>
                  {!r ? (
                    <tr><td colSpan={4}><LoadingBlock /></td></tr>
                  ) : r.materials.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="td py-8 text-center text-muted-foreground">
                        {r.squareFootage === 0 ? 'Enter a surface area to calculate quantities.' : 'No materials with coverage in this schedule.'}
                      </td>
                    </tr>
                  ) : (
                    r.materials.map((m) => (
                      <tr key={m.materialId} className="border-b last:border-0">
                        <td className="td font-medium">{m.name}</td>
                        <td className="td text-right tabular-nums whitespace-nowrap">{fixed(m.quantity, 3)} {m.unit}</td>
                        <td className="td text-right tabular-nums text-muted-foreground hidden sm:table-cell">{money(m.price)}</td>
                        <td className="td text-right tabular-nums">{money(m.cost)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
                {r && r.materials.length > 0 && (
                  <tfoot>
                    <tr className="border-t bg-muted/40 font-semibold">
                      <td className="td">Total material cost</td>
                      <td className="td" />
                      <td className="td hidden sm:table-cell" />
                      <td className="td text-right tabular-nums">{money(r.totalCost)}</td>
                    </tr>
                  </tfoot>
                )}
              </table>
            </div>
          </Card>
          {est.data.updatedAt && <p className="mt-2 text-xs text-muted-foreground no-print">Schedule last updated {dateTime(est.data.updatedAt)}</p>}
        </>
      )}

      {docsOpen && <EntityDocuments open onClose={() => setDocsOpen(false)} entityType="MaterialQuantity" entityId={0} groupId={groupId} title="Material Quantities Documents" />}
    </>
  )
}
