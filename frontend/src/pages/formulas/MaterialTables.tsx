import { useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, ArrowUpDown, Plus, Search } from 'lucide-react'
import clsx from 'clsx'
import { num } from '@/lib/format'
import type { InventoryBatch, MaterialOption } from './types'

type SortKey = 'category' | 'name' | 'code'

/**
 * The old site's Sort: DataTables compares the lower-cased text as stored, so names that start with a space come first
 * ("  M. L. Campbell…", " Solv Black") and "844 Quinacridone" comes before "BU BURNT UMBER".
 */
const text = (v?: string | null) => (v ?? '').toLowerCase()
const compare = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0)

export interface MaterialTablesProps {
  materials: MaterialOption[]
  loading: boolean
  /** Materials already in the formula are left out, as on the old site. */
  inFormula: Set<number>
  usesBatches: boolean
  batchesFor: (materialId: number) => InventoryBatch[]
  /** Pigments / Dyes as a share of the base grams ("10.84%"). */
  pigmentPct: string
  dyePct: string
  disabled: boolean
  disabledTitle?: string
  qtyPlaceholder: string
  /** Adds the material; resolves true when it was added (the table's Qty is then cleared). */
  onAdd: (material: MaterialOption, qty: string, batchNumber: string | null) => Promise<boolean>
}

/** Base Materials, Pigments and Dyes — the three pick lists of the old Edit Formula page, each with its own Search and Qty. */
export function MaterialTables(props: MaterialTablesProps) {
  const { materials, inFormula } = props
  const available = useMemo(() => materials.filter((m) => !inFormula.has(m.id)), [materials, inFormula])
  const bases = useMemo(() => available.filter((m) => m.materialType === 1 || m.materialType === 7), [available])
  const pigments = useMemo(() => available.filter((m) => m.materialType === 2), [available])
  const dyes = useMemo(() => available.filter((m) => m.materialType === 3), [available])

  return (
    <div className="space-y-3">
      <MaterialTable {...props} title="Base Materials" rows={bases} showCategory />
      {/* Side by side only when each table still has room for its batch column. */}
      <div className="grid gap-3 2xl:grid-cols-2">
        <MaterialTable {...props} title="Pigments" rows={pigments} share={props.pigmentPct} />
        <MaterialTable {...props} title="Dyes" rows={dyes} share={props.dyePct} />
      </div>
    </div>
  )
}

function MaterialTable({
  title, rows, showCategory, share, loading, usesBatches, batchesFor, disabled, disabledTitle, qtyPlaceholder, onAdd,
}: MaterialTablesProps & { title: string; rows: MaterialOption[]; showCategory?: boolean; share?: string }) {
  const [search, setSearch] = useState('')
  const [qty, setQty] = useState('')
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean }>({ key: 'name', desc: false })
  const [batchOf, setBatchOf] = useState<Record<number, string>>({})
  const [busy, setBusy] = useState<number | null>(null)

  const shown = useMemo(() => {
    const s = search.trim().toLowerCase()
    const list = s
      ? rows.filter((m) => [showCategory ? m.categoryName : null, m.productName, m.productCode].some((v) => text(v).includes(s)))
      : rows
    const keyOf = (m: MaterialOption) =>
      sort.key === 'category' ? text(m.categoryName) : sort.key === 'code' ? text(m.productCode) : text(m.sortName ?? m.productName)
    const sorted = [...list].sort((a, b) => compare(keyOf(a), keyOf(b)) || a.id - b.id)
    return sort.desc ? sorted.reverse() : sorted
  }, [rows, search, sort, showCategory])

  async function add(m: MaterialOption) {
    setBusy(m.id)
    try {
      const batches = batchesFor(m.id)
      // The row's dropdown starts on the latest batch (the legacy default); "" there means that one.
      const chosen = batchOf[m.id] || (batches.length ? batches[batches.length - 1].batchNumber : '')
      if (await onAdd(m, qty, chosen || null)) setQty('')
    } finally {
      setBusy(null)
    }
  }

  const header = (key: SortKey, label: string, className?: string) => (
    <th className={clsx('th', className)}>
      <button
        type="button"
        className="inline-flex items-center gap-1 uppercase tracking-[inherit] hover:text-foreground"
        onClick={() => setSort((s) => ({ key, desc: s.key === key ? !s.desc : false }))}
      >
        {label}
        {sort.key !== key ? <ArrowUpDown className="h-3 w-3 opacity-40" /> : sort.desc ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />}
      </button>
    </th>
  )

  return (
    <section className="rounded-lg border" aria-label={title}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b px-3 py-2">
        <h3 className="text-sm font-semibold">
          {title} <span className="ml-1 text-xs font-normal text-muted-foreground">{rows.length}</span>
        </h3>
        {share !== undefined && <span className="text-xs font-medium text-muted-foreground">{share}</span>}
      </div>
      <div className="flex flex-wrap items-center gap-2 px-3 py-2">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input className="input h-8 pl-8 text-sm" placeholder="Search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label={`Search ${title}`} />
        </div>
        <label className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
          Qty
          <input
            className="input h-8 w-28 text-sm tabular-nums"
            inputMode="decimal"
            placeholder={qtyPlaceholder}
            value={qty}
            onChange={(e) => setQty(e.target.value)}
            aria-label={`${title} quantity`}
          />
        </label>
      </div>
      <div className="max-h-56 overflow-auto border-t">
        <table className="w-full text-sm">
          <thead className="sticky top-0 z-10 bg-card">
            <tr>
              {showCategory && header('category', 'Material Category')}
              {header('name', 'Product Name')}
              {header('code', 'Product #')}
              {usesBatches && <th className="th">Batch #</th>}
              <th className="th w-10" />
            </tr>
          </thead>
          <tbody>
            {shown.map((m) => {
              const batches = usesBatches ? batchesFor(m.id) : []
              return (
                <tr key={m.id} className="border-t hover:bg-muted/40">
                  {showCategory && <td className="td py-1.5">{m.categoryName ?? '—'}</td>}
                  <td className="td min-w-[10rem] py-1.5">
                    {m.productName}
                    {m.materialType === 7 && <span className="badge ml-1.5 bg-muted text-muted-foreground">Product</span>}
                  </td>
                  <td className="td py-1.5 whitespace-nowrap">{m.productCode}</td>
                  {usesBatches && (
                    <td className="td py-1">
                      {batches.length === 0 ? (
                        <span className="text-xs text-muted-foreground">No Inventory Setup</span>
                      ) : (
                        <select
                          className="input h-7 w-40 py-0 text-xs"
                          value={batchOf[m.id] ?? batches[batches.length - 1].batchNumber}
                          onChange={(e) => setBatchOf((b) => ({ ...b, [m.id]: e.target.value }))}
                          aria-label={`Batch # for ${m.productName}`}
                        >
                          {batches.map((b) => (
                            <option key={b.batchNumber} value={b.batchNumber}>
                              #{b.batchNumber} - {num(b.onHand, 2)} Gal
                            </option>
                          ))}
                        </select>
                      )}
                    </td>
                  )}
                  <td className="td py-1 text-right">
                    <button
                      type="button"
                      className="btn-icon h-7 w-7"
                      onClick={() => void add(m)}
                      disabled={disabled || busy !== null}
                      title={disabledTitle ?? `Add ${m.productName} to the formula`}
                      aria-label={`Add ${m.productName}`}
                    >
                      <Plus className="h-4 w-4" />
                    </button>
                  </td>
                </tr>
              )
            })}
            {shown.length === 0 && (
              <tr>
                <td className="td py-4 text-center text-xs text-muted-foreground" colSpan={(showCategory ? 3 : 2) + (usesBatches ? 1 : 0) + 1}>
                  {loading ? 'Loading materials…' : search ? 'No matching materials' : 'No materials available'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
