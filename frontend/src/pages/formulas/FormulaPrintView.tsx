import { ArrowLeft, Pencil, Printer } from 'lucide-react'
import { money, num } from '@/lib/format'
import type { FormulaTotals } from './formulaMath'

export interface PrintLine {
  key: string
  name: string
  code?: string | null
  type: string
  grams: number
  percent: number
  gallons: number
}

export interface PrintData {
  groupName: string
  name: string
  number?: string
  customerName?: string
  categoryName?: string | null
  substrate?: string
  containerType?: string
  batchSize: number
  isComplete: boolean
  notes?: string
  spinDeltaE: number | null
  spexDeltaE: number | null
  lines: PrintLine[]
  totals: FormulaTotals
}

function Info({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <div>
      <div className="text-[10px] font-semibold uppercase tracking-wide text-slate-500">{label}</div>
      <div className="text-sm font-medium">{value === undefined || value === null || value === '' ? '—' : value}</div>
    </div>
  )
}

/**
 * Printable formula card + can label (Layout hides the sidebar/header when printing; controls are `no-print`).
 * Always rendered as white paper so the preview matches the printout in dark mode too.
 */
export function FormulaPrintView({ data, onBack, onEdit, onPrint }: { data: PrintData; onBack: () => void; onEdit?: () => void; onPrint: () => void }) {
  const today = new Date().toLocaleDateString('en-US')
  const t = data.totals
  return (
    <div>
      <div className="no-print mb-4 flex flex-wrap items-center gap-2">
        <button className="btn-secondary" onClick={onBack}>
          <ArrowLeft className="h-4 w-4" /> Back
        </button>
        {onEdit && (
          <button className="btn-secondary" onClick={onEdit}>
            <Pencil className="h-4 w-4" /> Open editor
          </button>
        )}
        <div className="flex-1" />
        <button className="btn-primary" onClick={onPrint}>
          <Printer className="h-4 w-4" /> Print
        </button>
      </div>

      <article className="mx-auto max-w-3xl rounded-lg border bg-white p-6 text-slate-900 shadow-card print:max-w-none print:border-0 print:p-0 print:shadow-none">
        <header className="flex items-start justify-between gap-4 border-b-2 border-slate-900 pb-3">
          <div>
            <div className="text-[11px] font-bold uppercase tracking-[0.2em] text-orange-600">Finish Genius · Formula Card</div>
            <h1 className="mt-1 text-2xl font-bold leading-tight">{data.name || 'Untitled formula'}</h1>
            <div className="text-sm text-slate-600">{data.groupName}</div>
          </div>
          <div className="text-right text-xs text-slate-600">
            <div>Printed {today}</div>
            <div className="mt-1 inline-block rounded border border-slate-400 px-2 py-0.5 font-semibold">{data.isComplete ? 'COMPLETE' : 'INCOMPLETE'}</div>
          </div>
        </header>

        <section className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Info label="Formula #" value={data.number} />
          <Info label="Customer" value={data.customerName} />
          <Info label="Category" value={data.categoryName} />
          <Info label="Substrate" value={data.substrate} />
          <Info label="Container" value={data.containerType} />
          <Info label="Batch size" value={data.batchSize ? `${num(data.batchSize, 2)} g` : null} />
          <Info label="Spin ΔE" value={data.spinDeltaE ?? null} />
          <Info label="Spex ΔE" value={data.spexDeltaE ?? null} />
        </section>

        <table className="mt-5 w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-slate-400 text-left text-[11px] uppercase tracking-wide text-slate-500">
              <th className="py-1.5 pr-2">#</th>
              <th className="py-1.5 pr-2">Material</th>
              <th className="py-1.5 pr-2">Type</th>
              <th className="py-1.5 pr-2 text-right">Grams</th>
              <th className="py-1.5 pr-2 text-right">%</th>
              <th className="py-1.5 text-right">Gallons</th>
            </tr>
          </thead>
          <tbody>
            {data.lines.length === 0 && (
              <tr>
                <td colSpan={6} className="py-4 text-center text-slate-500">
                  No ingredients.
                </td>
              </tr>
            )}
            {data.lines.map((l, i) => (
              <tr key={l.key} className="border-b border-slate-200 break-inside-avoid">
                <td className="py-1.5 pr-2 tabular-nums">{i + 1}</td>
                <td className="py-1.5 pr-2">
                  <div className="font-medium">{l.name}</div>
                  {l.code && <div className="text-xs text-slate-500">{l.code}</div>}
                </td>
                <td className="py-1.5 pr-2">{l.type}</td>
                <td className="py-1.5 pr-2 text-right tabular-nums">{num(l.grams, 2)}</td>
                <td className="py-1.5 pr-2 text-right tabular-nums">{num(l.percent, 2)}</td>
                <td className="py-1.5 text-right tabular-nums">{num(l.gallons, 4)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t-2 border-slate-900 font-semibold">
              <td colSpan={3} className="py-1.5">
                Total
              </td>
              <td className="py-1.5 pr-2 text-right tabular-nums">{num(t.totalGrams, 2)}</td>
              <td className="py-1.5 pr-2 text-right tabular-nums">{t.totalGrams > 0 ? '100' : '0'}</td>
              <td className="py-1.5 text-right tabular-nums">{num(t.totalGallons, 4)}</td>
            </tr>
          </tfoot>
        </table>

        <section className="mt-4 grid grid-cols-2 gap-3 rounded-md bg-slate-50 p-3 sm:grid-cols-4 print:bg-transparent print:p-0">
          <Info label="Total weight" value={`${num(t.totalGrams, 2)} g (${num(t.totalPounds, 3)} lb)`} />
          <Info label="VOC (lb/gal)" value={num(t.voc, 3)} />
          <Info label="HAP (lb/gal)" value={num(t.hap, 3)} />
          <Info label="TAP (lb/gal)" value={num(t.tap, 3)} />
          <Info label="Material cost" value={money(t.materialCost)} />
          <Info label="Formula price" value={money(t.price)} />
          <Info label="Cost / gal" value={money(t.costPerGallon)} />
          <Info label="Price / gal" value={money(t.pricePerGallon)} />
        </section>

        {data.notes && (
          <section className="mt-4 break-inside-avoid">
            <div className="text-[10px] font-semibold uppercase tracking-wide text-slate-500">Notes</div>
            <p className="whitespace-pre-line text-sm">{data.notes}</p>
          </section>
        )}

        {/* Can label — cut along the dashed line */}
        <section className="mt-6 break-inside-avoid">
          <div className="mb-1 text-[10px] uppercase tracking-wide text-slate-400">✂ Formula can label</div>
          <div className="w-full max-w-[4in] rounded border-2 border-dashed border-slate-500 p-3">
            <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-orange-600">{data.groupName}</div>
            <div className="text-lg font-bold leading-tight">{data.name || 'Untitled formula'}</div>
            <div className="mt-1 grid grid-cols-2 gap-x-3 gap-y-0.5 text-xs">
              <span>Formula #: <b>{data.number || '—'}</b></span>
              <span>Date: <b>{today}</b></span>
              <span className="col-span-2">Customer: <b>{data.customerName || '—'}</b></span>
              <span>Net: <b>{num(t.totalGrams, 1)} g</b></span>
              <span>VOC: <b>{num(t.voc, 2)} lb/gal</b></span>
              <span className="col-span-2 mt-1">Mixed by: ______________________</span>
            </div>
          </div>
        </section>
      </article>
    </div>
  )
}
