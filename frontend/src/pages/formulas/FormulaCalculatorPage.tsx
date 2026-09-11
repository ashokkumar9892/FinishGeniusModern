import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, Calculator, Printer, RotateCcw } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { num } from '@/lib/format'
import { Card, ErrorBanner, Field, LoadingBlock, Note, PageHeader } from '@/components/ui'
import { batchScaleFactor, batchValueOf, computeTotals, flOzOf, GRAMS_PER_POUND, isWeightBatchType, parseNum, typeLetter, typeOrder } from './formulaMath'
import { BATCH_TYPES, type FormulaDetail } from './types'

/**
 * Formula batch calculator (legacy Formulations/Calculator view, opened from My Work): pick a batch size in any unit or
 * reweigh the batch, and read the grams / fl oz of every ingredient. Nothing is saved to the formula.
 */
export default function FormulaCalculatorPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const detail = useQuery({
    queryKey: ['formula', id],
    queryFn: () => api.get<FormulaDetail>(`/formulas/${id}`).then((r) => r.data),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  })
  const [batchType, setBatchType] = useState(2)
  const [factor, setFactor] = useState(1)
  const [draft, setDraft] = useState<string | null>(null)
  const [reweigh, setReweigh] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (detail.data) setBatchType(detail.data.batchType || 2)
  }, [detail.data])

  const lines = useMemo(
    () =>
      [...(detail.data?.ingredients ?? [])]
        .sort((a, b) => typeOrder(a.materialType) - typeOrder(b.materialType) || a.sequence - b.sequence)
        .map((i) => ({ ...i, scaled: Math.round(i.grams * factor * 10000) / 10000 })),
    [detail.data, factor],
  )
  const totals = computeTotals(lines.map((l) => ({ grams: l.scaled, density: l.density, price: l.price, voc: l.voc, hap: l.hap, tap: l.tap })), 0, 0)
  const value = batchValueOf(batchType, totals.totalGrams, totals.totalGallons)
  const unit = BATCH_TYPES.find((b) => b.value === batchType)?.unit ?? 'g'
  const noDensity = lines.filter((l) => l.density <= 0)

  const apply = () => {
    if (draft === null) return
    const v = parseNum(draft)
    setDraft(null)
    setError(null)
    if (v === null) return
    if (!isWeightBatchType(batchType) && noDensity.length) return setError('A volume batch size needs the density of every material.')
    const f = batchScaleFactor(batchType, v, totals.totalGrams, totals.totalGallons)
    if (!f) return setError('Enter a batch size greater than 0.')
    setFactor((x) => x * f)
  }

  const go = () => {
    setError(null)
    const v = parseNum(reweigh)
    const base = lines.reduce((s, l) => s + l.grams, 0)
    if (v === null || v <= 0) return setError('No valid weight entered for batch')
    if (base <= 0) return setError('This formula has no ingredients.')
    setFactor(v / base)
    setReweigh('')
  }

  if (detail.isLoading) return <LoadingBlock label="Loading formula…" />
  if (detail.isError || !detail.data)
    return (
      <>
        <PageHeader title="Calculator" breadcrumbs={[['Formulas', '/formulas'], 'Calculator']} />
        <ErrorBanner message={errorMessage(detail.error)} />
      </>
    )
  const d = detail.data

  return (
    <>
      <PageHeader
        title={`Calculator: ${d.name}${d.number ? ` - ${d.number}` : ''}`}
        breadcrumbs={[['Formulas', '/formulas'], [d.name, `/formulas/${d.id}`], 'Calculator']}
        subtitle="Scale the formula to any batch size. Nothing here changes the saved formula."
        actions={
          <>
            <button className="btn-secondary" onClick={() => navigate(`/formulas/${d.id}`)}>
              <ArrowLeft className="h-4 w-4" /> Back to Formula
            </button>
            <button className="btn-secondary" onClick={() => setFactor(1)} disabled={factor === 1}>
              <RotateCcw className="h-4 w-4" /> Reset
            </button>
            <button className="btn-primary" onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print
            </button>
          </>
        }
      />
      <ErrorBanner message={error} />
      <Card title={<span className="inline-flex items-center gap-2"><Calculator className="h-4 w-4" /> Batch Size</span>}>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Field label={`Weight (${unit})`}>
            <input
              className="input tabular-nums"
              inputMode="decimal"
              value={draft ?? String(Math.round(value * 10000) / 10000)}
              onChange={(e) => setDraft(e.target.value)}
              onBlur={apply}
              onKeyDown={(e) => e.key === 'Enter' && apply()}
              aria-label="Batch size"
            />
          </Field>
          <Field label="Batch type">
            <select className="input" value={batchType} onChange={(e) => setBatchType(Number(e.target.value))}>
              {BATCH_TYPES.map((b) => (
                <option key={b.value} value={b.value}>
                  {b.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Reweigh Batch (g)">
            <div className="flex gap-1.5">
              <input className="input tabular-nums" inputMode="decimal" value={reweigh} onChange={(e) => setReweigh(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && go()} aria-label="Reweigh Batch" />
              <button className="btn-primary" onClick={go}>
                GO
              </button>
            </div>
          </Field>
          <Field label="Total Formula Weight (g)">
            <div className="flex h-9 items-center text-lg font-semibold tabular-nums">{num(totals.totalGrams, 2)}</div>
          </Field>
        </div>
        {factor !== 1 && <p className="mt-2 text-xs text-muted-foreground">Scale factor × {num(factor, 4)} of the saved formula.</p>}
        {noDensity.length > 0 && (
          <div className="mt-3">
            <Note>No density (lb/gal) for: {noDensity.map((l) => l.productName).join(', ')}. Volume batch types and Fl Oz are not available for them.</Note>
          </div>
        )}
      </Card>
      <Card title="Color Formula" className="mt-5" bodyClassName="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/60">
              <tr>
                <th className="th w-10 text-center">T</th>
                <th className="th">Prod Name</th>
                <th className="th">Prod #</th>
                <th className="th text-right">Grams</th>
                <th className="th text-right">lbs</th>
                <th className="th text-right">Fl Oz</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((l) => {
                const oz = flOzOf(l.scaled, l.density)
                return (
                  <tr key={l.id} className="border-b last:border-0">
                    <td className="td text-center font-semibold">{typeLetter(l.materialType)}</td>
                    <td className="td font-medium">{l.productName}</td>
                    <td className="td">{l.productCode}</td>
                    <td className="td text-right tabular-nums">{num(l.scaled, 4)}</td>
                    <td className="td text-right tabular-nums">{num(l.scaled / GRAMS_PER_POUND, 3)}</td>
                    <td className="td text-right tabular-nums">{oz === null ? 'Error' : num(oz, 4)}</td>
                  </tr>
                )
              })}
            </tbody>
            <tfoot className="border-t-2 bg-muted/40 font-semibold">
              <tr>
                <td className="td" colSpan={3}>
                  Total
                </td>
                <td className="td text-right tabular-nums">{num(totals.totalGrams, 4)}</td>
                <td className="td text-right tabular-nums">{num(totals.totalPounds, 3)}</td>
                <td className="td text-right tabular-nums">{num(totals.totalGallons * 128, 4)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </Card>
    </>
  )
}
