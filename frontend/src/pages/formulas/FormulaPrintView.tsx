import type { ReactNode } from 'react'
import { ArrowLeft, Pencil, Printer } from 'lucide-react'
import { fileUrl } from '@/lib/api'
import { money, num } from '@/lib/format'
import { logoSrc } from '@/components/Layout'
import type { FormulaTotals } from './formulaMath'

export interface PrintLine {
  key: string
  /** "Base", "Pigment", "Dye", "Product" — already in legacy print order. */
  type: string
  name: string
  number?: string | null
  grams: number
  flOz: number | null
}

export interface DeltaSet {
  l: number | null
  a: number | null
  b: number | null
  e: number | null
}

export interface PrintData {
  groupName: string
  groupLogoFile?: string | null
  name: string
  number?: string
  employeeName?: string
  categoryName?: string | null
  mixedOn?: string | null
  createdOn?: string | null
  substrate?: string
  notes?: string
  customerName?: string
  purchaseOrderNumber?: string
  markUp: number
  containerPrice: number
  containerType?: string
  batchNumbers: string[]
  spex: DeltaSet
  spin: DeltaSet
  lines: PrintLine[]
  totals: FormulaTotals
}

export type PrintMode = 'formula' | 'label' | 'customerLabel'

const shortDate = (iso?: string | null) => {
  if (!iso) return ''
  const d = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z')
  return isNaN(d.getTime()) ? '' : d.toLocaleDateString('en-US')
}
const dateAndTime = (iso?: string | null) => {
  if (!iso) return ''
  const d = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z')
  return isNaN(d.getTime()) ? '' : d.toLocaleString('en-US')
}
const dash = (v?: string | number | null) => (v === undefined || v === null || v === '' ? '-' : v)
const hasDelta = (d: DeltaSet) => [d.l, d.a, d.b, d.e].some((v) => v !== null && v !== undefined)

function Prop({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <div className="flex gap-3 border-b border-slate-200 py-1.5">
      <div className="w-36 shrink-0 text-[11px] font-semibold uppercase tracking-wide text-slate-500">{label}</div>
      <div className="text-sm font-medium">{dash(value)}</div>
    </div>
  )
}

function DeltaTable({ title, d }: { title: string; d: DeltaSet }) {
  return (
    <section className="mt-4 break-inside-avoid">
      <h2 className="text-xs font-bold uppercase tracking-wide">{title}</h2>
      <table className="mt-1 w-full border border-slate-300 text-sm">
        <thead className="bg-slate-50 text-left text-[11px] uppercase text-slate-500">
          <tr>
            <th className="px-2 py-1">Delta L*</th>
            <th className="px-2 py-1">Delta A*</th>
            <th className="px-2 py-1">Delta B*</th>
            <th className="px-2 py-1">Delta E*</th>
          </tr>
        </thead>
        <tbody>
          <tr className="text-base">
            <td className="px-2 py-1">{dash(d.l)}</td>
            <td className="px-2 py-1">{dash(d.a)}</td>
            <td className="px-2 py-1">{dash(d.b)}</td>
            <td className="px-2 py-1">{dash(d.e)}</td>
          </tr>
        </tbody>
      </table>
    </section>
  )
}

/** Legacy Print.cshtml ("Formula" PDF). */
function FormulaSheet({ data }: { data: PrintData }) {
  const t = data.totals
  return (
    <article className="mx-auto max-w-3xl rounded-lg border bg-white p-6 text-slate-900 shadow-card print:max-w-none print:border-0 print:p-0 print:shadow-none">
      <header className="flex items-start justify-between gap-4 border-b-2 border-slate-900 pb-3">
        <div>
          <h1 className="text-3xl font-bold">Formula</h1>
          <p className="mt-2 text-xs text-slate-600">Created On: {dateAndTime(data.createdOn)}</p>
        </div>
        <img src={data.groupLogoFile ? fileUrl(data.groupLogoFile) : logoSrc} alt={data.groupName} className="max-h-24 max-w-[12rem] object-contain" />
      </header>

      <section className="mt-4 grid gap-x-6 sm:grid-cols-2">
        <Prop label="Formula Name" value={data.name} />
        <Prop label="Group" value={data.groupName} />
      </section>
      <section className="mt-3 grid gap-x-6 rounded border border-slate-300 p-3 sm:grid-cols-2">
        <Prop label="Number" value={data.number} />
        <Prop label="Employee Name" value={data.employeeName} />
        <Prop label="Material Type" value="Formulation" />
        <Prop label="Product Category" value={data.categoryName} />
        <Prop label="Mixed On" value={dateAndTime(data.mixedOn)} />
        <Prop label="Substrate" value={data.substrate} />
        {data.notes && data.notes !== '-' && (
          <div className="sm:col-span-2">
            <Prop label="Notes" value={data.notes} />
          </div>
        )}
      </section>

      <h2 className="mt-5 text-xs font-bold uppercase tracking-wide">Color Formula</h2>
      <table className="mt-1 w-full border border-slate-300 text-sm">
        <thead className="bg-slate-50 text-left text-[11px] uppercase text-slate-500">
          <tr>
            <th className="px-2 py-1">Type</th>
            <th className="px-2 py-1">Name</th>
            <th className="px-2 py-1">Number</th>
            <th className="px-2 py-1 text-right">Grams</th>
            <th className="px-2 py-1 text-right">Kilograms</th>
            <th className="px-2 py-1 text-right">Fl Oz</th>
          </tr>
        </thead>
        <tbody>
          {data.lines.length === 0 && (
            <tr>
              <td colSpan={6} className="px-2 py-3 text-center text-slate-500">
                -
              </td>
            </tr>
          )}
          {data.lines.map((l) => (
            <tr key={l.key} className="break-inside-avoid border-t border-slate-200">
              <td className="px-2 py-1">{l.type}</td>
              <td className="px-2 py-1 font-medium">{l.name}</td>
              <td className="px-2 py-1">{dash(l.number)}</td>
              <td className="px-2 py-1 text-right tabular-nums">{num(l.grams, 3)}</td>
              <td className="px-2 py-1 text-right tabular-nums">{num(l.grams / 1000, 3)}</td>
              <td className="px-2 py-1 text-right tabular-nums">{l.flOz === null ? 'Error' : num(l.flOz, 2)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {t.price > 0 && (
        <section className="mt-4 break-inside-avoid">
          <h2 className="text-xs font-bold uppercase tracking-wide">Total</h2>
          <table className="mt-1 w-full border border-slate-300 text-sm">
            <thead className="bg-slate-50 text-left text-[11px] uppercase text-slate-500">
              <tr>
                <th className="px-2 py-1">Grams in batch</th>
                <th className="px-2 py-1">Mark-Up</th>
                <th className="px-2 py-1">Container price</th>
                <th className="px-2 py-1">Formula cost</th>
              </tr>
            </thead>
            <tbody>
              <tr className="text-base">
                <td className="px-2 py-1 tabular-nums">{num(t.totalGrams, 2)}</td>
                <td className="px-2 py-1 tabular-nums">{num(data.markUp, 2)}</td>
                <td className="px-2 py-1 tabular-nums">
                  {money(data.containerPrice)}
                  {data.containerType ? ` (${data.containerType})` : ''}
                </td>
                <td className="px-2 py-1 tabular-nums">{money(t.price)}</td>
              </tr>
            </tbody>
          </table>
        </section>
      )}

      {hasDelta(data.spex) && <DeltaTable title="SPEX" d={data.spex} />}
      {hasDelta(data.spin) && <DeltaTable title="SPIN" d={data.spin} />}
    </article>
  )
}

/** Legacy PrintLabel.cshtml — 4 × 6 in can label; the customer label shows the PO # and no ingredient table. */
export function FormulaLabel({ data, customer }: { data: PrintData; customer: boolean }) {
  const gallons = data.lines.reduce((s, l) => s + (l.flOz ?? 0), 0) / 128
  const r1 = (v: number) => (Math.round(v * 10) / 10).toFixed(1)
  const totals = data.lines.reduce(
    (s, l) => ({ grams: s.grams + Number(r1(l.grams)), lbs: s.lbs + Number(r1(l.grams * 0.00220462)), oz: s.oz + (l.flOz === null ? 0 : Number(r1(l.flOz))) }),
    { grams: 0, lbs: 0, oz: 0 },
  )
  return (
    <article className="formula-label mx-auto w-full max-w-[4in] break-inside-avoid border border-slate-300 bg-white p-3 text-slate-900 shadow-card print:max-w-none print:border-0 print:shadow-none">
      <div className="text-lg font-extrabold leading-tight">{data.groupName}</div>
      <div className="mt-1 text-xl font-extrabold leading-tight">{data.name}</div>
      <div className="mt-1 text-base">Formula #: {data.number}</div>
      {data.batchNumbers.map((b) => (
        <div key={b} className="text-base">
          Batch #: {b}
        </div>
      ))}
      {customer && <div className="text-base">PO #: {data.purchaseOrderNumber}</div>}
      <table className="mt-2 w-full text-sm">
        <tbody>
          <tr className="align-top">
            <td className="w-3/5 pb-1">
              Mixed By: <br /> <b>{data.employeeName}</b>
            </td>
            <td className="pb-1">
              Mixed On: <br /> <b>{shortDate(data.mixedOn)}</b>
            </td>
          </tr>
          <tr className="align-top">
            <td>
              Material Type: <br /> <b>{data.categoryName}</b>
            </td>
            <td>
              Mixed Quantity: <br /> <b>{(Math.round(gallons * 1000) / 1000).toString()} Gal</b>
            </td>
          </tr>
        </tbody>
      </table>
      {!customer && data.lines.length > 0 && (
        <div className="mt-2 border-2 border-slate-900 p-1">
          <table className="w-full border-collapse text-[11px]">
            <thead className="text-left">
              <tr>
                <th>Type</th>
                <th className="w-[35%]">Name</th>
                <th>Number</th>
                <th className="text-right">Grams</th>
                <th className="text-right">lbs</th>
                <th className="text-right">Fl Oz</th>
              </tr>
            </thead>
            <tbody>
              {data.lines.map((l) => (
                <tr key={l.key} className="border-t border-slate-900">
                  <td className="border-l border-dotted border-slate-900 pr-1">{l.type}</td>
                  <td className="border-l border-dotted border-slate-900 pr-1">{l.name}</td>
                  <td className="border-l border-dotted border-slate-900 pr-1">{l.number}</td>
                  <td className="border-l border-dotted border-slate-900 px-1 text-right tabular-nums">{r1(l.grams)}</td>
                  <td className="border-l border-dotted border-slate-900 px-1 text-right tabular-nums">{r1(l.grams * 0.00220462)}</td>
                  <td className="border-l border-dotted border-slate-900 text-right tabular-nums">{l.flOz === null ? 'Error' : r1(l.flOz)}</td>
                </tr>
              ))}
              <tr className="border-t border-slate-900 font-semibold">
                <td colSpan={3} className="pr-3 text-right">
                  Total :
                </td>
                <td className="px-1 text-right tabular-nums">{totals.grams.toFixed(1)}</td>
                <td className="px-1 text-right tabular-nums">{totals.lbs.toFixed(1)}</td>
                <td className="text-right tabular-nums">{totals.oz.toFixed(1)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </article>
  )
}

/**
 * Printable formula (legacy "Print"), formula can label and customer can label. The Layout hides the sidebar/header when
 * printing; controls are `no-print`. Always rendered as white paper so the preview matches the printout in dark mode too.
 */
export function FormulaPrintView({ data, mode, onBack, onEdit, onPrint, toolbar }: {
  data: PrintData
  mode: PrintMode
  onBack: () => void
  onEdit?: () => void
  onPrint: () => void
  toolbar?: ReactNode
}) {
  const label = mode !== 'formula'
  return (
    <div>
      {label && <style>{'@media print { @page { size: 4in 6in; margin: 0.15in; } }'}</style>}
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
        {toolbar}
        <button className="btn-primary" onClick={onPrint}>
          <Printer className="h-4 w-4" /> Print
        </button>
      </div>
      {label && (
        <p className="no-print mb-3 text-center text-xs text-muted-foreground">
          {mode === 'customerLabel' ? 'Customer Can Label' : 'Formula Can Label'} · 4 × 6 in
        </p>
      )}
      {mode === 'formula' ? <FormulaSheet data={data} /> : <FormulaLabel data={data} customer={mode === 'customerLabel'} />}
    </div>
  )
}
