import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowDown, ArrowLeft, ArrowUp, FileText, FlaskConical, Printer, Save, Scale, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime, money, num } from '@/lib/format'
import { Card, Checkbox, EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note, PageHeader, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { EntityDocuments } from '@/components/EntityDocuments'
import { useToast } from '@/components/toast'
import { CompositionDonut } from './CompositionDonut'
import { StatusBadge } from './FormulaModals'
import { FormulaPrintView, type PrintData } from './FormulaPrintView'
import { computeTotals, deltaEMatch, gallonsOf, parseNum, scaleGrams } from './formulaMath'
import { INGREDIENT_TYPES, type CategoryOption, type FormulaDetail, type MaterialOption } from './types'

const DELTA_KEYS = ['spinDeltaL', 'spinDeltaA', 'spinDeltaB', 'spinDeltaE', 'spexDeltaL', 'spexDeltaA', 'spexDeltaB', 'spexDeltaE'] as const
type DeltaKey = (typeof DELTA_KEYS)[number]

type HeaderState = {
  name: string
  number: string
  customerName: string
  categoryId: number | null
  batchSize: string
  containerType: string
  containerPrice: string
  markUp: string
  substrate: string
  notes: string
  isComplete: boolean
} & Record<DeltaKey, string>

interface Line {
  key: string
  materialId: number
  productName: string
  productCode?: string | null
  materialTypeLabel: string
  materialDeleted: boolean
  density: number
  price: number
  voc: number
  hap: number
  tap: number
  grams: string
}

const emptyHeader: HeaderState = {
  name: '', number: '', customerName: '', categoryId: null, batchSize: '', containerType: '', containerPrice: '0', markUp: '0',
  substrate: '', notes: '', isComplete: false,
  spinDeltaL: '', spinDeltaA: '', spinDeltaB: '', spinDeltaE: '', spexDeltaL: '', spexDeltaA: '', spexDeltaB: '', spexDeltaE: '',
}

const str = (v?: number | null) => (v === null || v === undefined ? '' : String(v))
let keySeq = 0
const nextKey = () => `l${++keySeq}`

function fromDetail(d: FormulaDetail): { header: HeaderState; lines: Line[] } {
  const header: HeaderState = {
    name: d.name, number: d.number ?? '', customerName: d.customerName ?? '', categoryId: d.categoryId ?? null,
    batchSize: d.batchSize ? str(d.batchSize) : '', containerType: d.containerType ?? '', containerPrice: str(d.containerPrice),
    markUp: str(d.markUp), substrate: d.substrate ?? '', notes: d.notes ?? '', isComplete: d.isComplete,
    spinDeltaL: str(d.spinDeltaL), spinDeltaA: str(d.spinDeltaA), spinDeltaB: str(d.spinDeltaB), spinDeltaE: str(d.spinDeltaE),
    spexDeltaL: str(d.spexDeltaL), spexDeltaA: str(d.spexDeltaA), spexDeltaB: str(d.spexDeltaB), spexDeltaE: str(d.spexDeltaE),
  }
  const lines = d.ingredients.map((i) => ({
    key: nextKey(), materialId: i.materialId, productName: i.productName, productCode: i.productCode, materialTypeLabel: i.materialTypeLabel,
    materialDeleted: i.materialDeleted, density: i.density, price: i.price, voc: i.voc, hap: i.hap, tap: i.tap, grams: str(i.grams),
  }))
  return { header, lines }
}

const snapshot = (h: HeaderState, lines: Line[]) => JSON.stringify([h, lines.map((l) => [l.materialId, l.grams])])
const badNumber = (s: string) => s.trim() !== '' && parseNum(s) === null

function validate(h: HeaderState, lines: Line[]) {
  const e: Record<string, string> = {}
  if (!h.name.trim()) e.name = 'Formula Name is required.'
  const nonNegative: [keyof HeaderState, string][] = [['batchSize', 'Batch Size'], ['containerPrice', 'Container price'], ['markUp', 'Mark-up']]
  for (const [k, label] of nonNegative) {
    const s = String(h[k])
    if (badNumber(s) || (parseNum(s) ?? 0) < 0) e[k] = `${label} must be a number of 0 or more.`
  }
  for (const k of DELTA_KEYS) if (badNumber(h[k])) e[k] = 'Not a number'
  if ((parseNum(h.spinDeltaE) ?? 0) < 0) e.spinDeltaE = 'ΔE cannot be negative'
  if ((parseNum(h.spexDeltaE) ?? 0) < 0) e.spexDeltaE = 'ΔE cannot be negative'
  for (const l of lines) {
    const g = parseNum(l.grams)
    if (g === null || g <= 0) e[`line-${l.key}`] = 'Enter grams > 0'
  }
  if (h.isComplete && lines.length === 0) e.isComplete = 'A formula without ingredients cannot be marked Complete.'
  return e
}

/** Number input with an optional unit prefix/suffix ($, %, g). */
function NumInput({ value, onChange, prefix, suffix, invalid, placeholder, className, autoFocus, ariaLabel }: {
  value: string
  onChange: (v: string) => void
  prefix?: string
  suffix?: string
  invalid?: boolean
  placeholder?: string
  className?: string
  autoFocus?: boolean
  ariaLabel?: string
}) {
  return (
    <div className={clsx('relative', className)}>
      {prefix && <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">{prefix}</span>}
      <input
        className={clsx('input tabular-nums', prefix && 'pl-6', suffix && 'pr-8', invalid && 'input-invalid')}
        inputMode="decimal"
        value={value}
        placeholder={placeholder}
        autoFocus={autoFocus}
        aria-label={ariaLabel}
        onChange={(e) => onChange(e.target.value)}
      />
      {suffix && <span className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">{suffix}</span>}
    </div>
  )
}

function SummaryRow({ label, value, strong, hint }: { label: string; value: ReactNode; strong?: boolean; hint?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5" title={hint}>
      <span className={clsx('text-sm', strong ? 'font-semibold' : 'text-muted-foreground')}>{label}</span>
      <span className={clsx('tabular-nums', strong ? 'text-base font-semibold' : 'text-sm font-medium')}>{value}</span>
    </div>
  )
}

export default function FormulaEditPage() {
  const { id = 'new' } = useParams()
  const isNew = id === 'new'
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()
  const { groupId, group } = useGroup()

  const [header, setHeader] = useState<HeaderState>(emptyHeader)
  const [lines, setLines] = useState<Line[]>([])
  const [baseline, setBaseline] = useState(() => snapshot(emptyHeader, []))
  const [loadedAt, setLoadedAt] = useState(0)
  const [submitted, setSubmitted] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const [lastAdded, setLastAdded] = useState<string | null>(null)
  const [docsOpen, setDocsOpen] = useState(false)
  const [scaleOpen, setScaleOpen] = useState(false)
  const [printing, setPrinting] = useState(params.get('print') === '1')
  const [autoPrint, setAutoPrint] = useState(params.get('print') === '1')

  const detail = useQuery({
    queryKey: ['formula', id],
    queryFn: () => api.get<FormulaDetail>(`/formulas/${id}`).then((r) => r.data),
    enabled: !isNew,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  })

  // Populate the form whenever fresh server data arrives (first load, after save).
  useEffect(() => {
    if (isNew || !detail.data || detail.dataUpdatedAt === loadedAt) return
    const { header: h, lines: l } = fromDetail(detail.data)
    setHeader(h)
    setLines(l)
    setBaseline(snapshot(h, l))
    setLoadedAt(detail.dataUpdatedAt)
    setSubmitted(false)
    setServerError(null)
  }, [isNew, detail.data, detail.dataUpdatedAt, loadedAt])

  // "New" resets when the page is (re)opened for a new formula or the header group changes.
  useEffect(() => {
    if (!isNew) return
    setHeader(emptyHeader)
    setLines([])
    setBaseline(snapshot(emptyHeader, []))
    setSubmitted(false)
    setServerError(null)
  }, [isNew, groupId])

  const gid = isNew ? groupId : (detail.data?.groupId ?? 0)
  const groupName = isNew ? (group?.name ?? '') : (detail.data?.groupName ?? '')

  const materialsQ = useQuery({
    queryKey: ['materials', gid],
    queryFn: () => api.get<MaterialOption[]>('/materials', { params: { groupId: gid } }).then((r) => r.data),
    enabled: gid > 0,
  })
  const categoriesQ = useQuery({
    queryKey: ['material-categories', gid, 6],
    queryFn: () => api.get<CategoryOption[]>('/material-categories', { params: { groupId: gid, type: 6 } }).then((r) => r.data),
    enabled: gid > 0,
  })

  const dirty = snapshot(header, lines) !== baseline
  useEffect(() => {
    if (!dirty) return
    const h = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', h)
    return () => window.removeEventListener('beforeunload', h)
  }, [dirty])

  const set = <K extends keyof HeaderState>(k: K, v: HeaderState[K]) => setHeader((h) => ({ ...h, [k]: v }))

  // ---------------- live maths
  const markUp = parseNum(header.markUp) ?? 0
  const containerPrice = parseNum(header.containerPrice) ?? 0
  const batchSize = parseNum(header.batchSize) ?? 0
  const gramsOf = (l: Line) => Math.max(0, parseNum(l.grams) ?? 0)
  const totals = useMemo(
    () => computeTotals(lines.map((l) => ({ grams: gramsOf(l), density: l.density, price: l.price, voc: l.voc, hap: l.hap, tap: l.tap })), markUp, containerPrice),
    [lines, markUp, containerPrice],
  )
  const errors = useMemo(() => validate(header, lines), [header, lines])
  const showErr = (k: string) => (submitted ? errors[k] : undefined)

  // ---------------- ingredients
  const inFormula = useMemo(() => new Set(lines.map((l) => l.materialId)), [lines])
  const materialOptions = useMemo(
    () =>
      (materialsQ.data ?? [])
        .filter((m) => INGREDIENT_TYPES.includes(m.materialType) && !inFormula.has(m.id))
        .sort((a, b) => a.productName.localeCompare(b.productName))
        .map((m) => ({
          value: m.id,
          label: m.productName,
          sub: [m.materialTypeLabel, m.productCode, `${num(m.density)} lb/gal`, `${money(m.price)}/gal`].filter(Boolean).join(' · '),
        })),
    [materialsQ.data, inFormula],
  )

  const addMaterial = (materialId: number | null) => {
    const m = materialsQ.data?.find((x) => x.id === materialId)
    if (!m) return
    const key = nextKey()
    setLines((ls) => [
      ...ls,
      {
        key, materialId: m.id, productName: m.productName, productCode: m.productCode, materialTypeLabel: m.materialTypeLabel, materialDeleted: false,
        density: m.density, price: m.price, voc: m.voc, hap: m.hap, tap: m.tap, grams: '',
      },
    ])
    setLastAdded(key)
  }
  const updateGrams = (key: string, grams: string) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, grams } : l)))
  const removeLine = (key: string) => setLines((ls) => ls.filter((l) => l.key !== key))
  const moveLine = (index: number, dir: -1 | 1) =>
    setLines((ls) => {
      const j = index + dir
      if (j < 0 || j >= ls.length) return ls
      const next = [...ls]
      ;[next[index], next[j]] = [next[j], next[index]]
      return next
    })

  const applyScale = (target: number, setBatch: boolean) => {
    const scaled = scaleGrams(lines.map(gramsOf), target)
    setLines((ls) => ls.map((l, i) => ({ ...l, grams: String(scaled[i]) })))
    if (setBatch) set('batchSize', String(target))
    toast.success(`Batch scaled to ${num(target, 2)} g.`)
  }

  // ---------------- save
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        groupId: gid,
        categoryId: header.categoryId,
        name: header.name.trim(),
        number: header.number.trim() || null,
        customerName: header.customerName.trim() || null,
        isComplete: header.isComplete,
        batchSize,
        containerType: header.containerType.trim() || null,
        containerPrice,
        markUp,
        substrate: header.substrate.trim() || null,
        notes: header.notes.trim() || null,
        ...Object.fromEntries(DELTA_KEYS.map((k) => [k, parseNum(header[k])])),
        ingredients: lines.map((l, i) => ({ materialId: l.materialId, grams: parseNum(l.grams) ?? 0, sequence: i + 1 })),
      }
      const res = isNew
        ? await api.post<{ message: string; id: number }>('/formulas', body)
        : await api.put<{ message: string; id: number }>(`/formulas/${id}`, body)
      return res.data
    },
    onSuccess: (res) => {
      toast.success(res.message)
      setServerError(null)
      setBaseline(snapshot(header, lines))
      qc.invalidateQueries({ queryKey: ['formulas'] })
      qc.invalidateQueries({ queryKey: ['documents'] })
      if (isNew) navigate(`/formulas/${res.id}`, { replace: true })
      else qc.invalidateQueries({ queryKey: ['formula', id] })
    },
    onError: (e) => {
      setServerError(errorMessage(e))
      window.scrollTo({ top: 0, behavior: 'smooth' })
    },
  })

  const submit = () => {
    setSubmitted(true)
    if (Object.keys(errors).length > 0) {
      setServerError('Please fix the highlighted fields.')
      return
    }
    save.mutate()
  }

  const goBack = () => {
    if (dirty && !window.confirm('You have unsaved changes. Leave without saving?')) return
    navigate('/formulas')
  }

  // ---------------- print
  const printData: PrintData = {
    groupName,
    name: header.name,
    number: header.number,
    customerName: header.customerName,
    categoryName: categoriesQ.data?.find((c) => c.id === header.categoryId)?.name ?? detail.data?.categoryName,
    substrate: header.substrate,
    containerType: header.containerType,
    batchSize,
    isComplete: header.isComplete,
    notes: header.notes,
    spinDeltaE: parseNum(header.spinDeltaE),
    spexDeltaE: parseNum(header.spexDeltaE),
    totals,
    lines: lines.map((l) => ({
      key: l.key, name: l.productName, code: l.productCode, type: l.materialTypeLabel, grams: gramsOf(l),
      percent: totals.totalGrams > 0 ? (gramsOf(l) / totals.totalGrams) * 100 : 0, gallons: gallonsOf(gramsOf(l), l.density),
    })),
  }
  const printReady = isNew || loadedAt > 0
  useEffect(() => {
    if (!printing || !autoPrint || !printReady) return
    setAutoPrint(false)
    const t = setTimeout(() => window.print(), 250)
    return () => clearTimeout(t)
  }, [printing, autoPrint, printReady])

  // ---------------- render
  if (!isNew && detail.isLoading) return <LoadingBlock label="Loading formula…" />
  if (!isNew && detail.isError)
    return (
      <>
        <PageHeader title="Formula" breadcrumbs={[['Formulas', '/formulas'], 'Formula']} />
        <ErrorBanner message={errorMessage(detail.error)} />
        <button className="btn-secondary" onClick={() => navigate('/formulas')}>
          <ArrowLeft className="h-4 w-4" /> Back to formulas
        </button>
      </>
    )

  if (printing)
    return (
      <FormulaPrintView
        data={printData}
        onPrint={() => window.print()}
        onBack={() => (params.get('print') === '1' ? navigate('/formulas') : setPrinting(false))}
        onEdit={
          params.get('print') === '1'
            ? () => {
                setParams({}, { replace: true })
                setPrinting(false)
              }
            : undefined
        }
      />
    )

  const categoryOptions = (categoriesQ.data ?? []).map((c) => ({ value: c.id, label: c.name }))
  const batchMismatch = batchSize > 0 && totals.totalGrams > 0 && Math.abs(totals.totalGrams - batchSize) > 0.005

  return (
    <>
      <PageHeader
        title={isNew ? 'New Formulation' : header.name || detail.data?.name || 'Formula'}
        breadcrumbs={[['Formulas', '/formulas'], isNew ? 'New Formulation' : header.name || 'Formula']}
        subtitle={
          <span className="inline-flex flex-wrap items-center gap-2">
            <span>Formulation workspace · {groupName}</span>
            <StatusBadge complete={header.isComplete} />
            {dirty && <span className="badge bg-muted text-muted-foreground">Unsaved changes</span>}
            {!isNew && detail.data?.updatedAt && <span className="text-xs">Last saved {dateTime(detail.data.updatedAt)}</span>}
          </span>
        }
        actions={
          <>
            <button className="btn-secondary" onClick={goBack}>
              <ArrowLeft className="h-4 w-4" /> Back
            </button>
            <button className="btn-secondary" onClick={() => setDocsOpen(true)} disabled={isNew} title={isNew ? 'Save the formula first' : 'Documents'}>
              <FileText className="h-4 w-4" /> Documents
            </button>
            <button className="btn-secondary" onClick={() => { setPrinting(true); setAutoPrint(true) }}>
              <Printer className="h-4 w-4" /> Print
            </button>
            <button className="btn-primary" onClick={submit} disabled={save.isPending || (!dirty && !isNew)}>
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        }
      />

      <ErrorBanner message={serverError} />

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px] xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          {/* Header */}
          <Card title="Formula details">
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
              <Field label="Formula Name" required error={showErr('name')} className="sm:col-span-2 xl:col-span-1">
                <input className={clsx('input', showErr('name') && 'input-invalid')} value={header.name} maxLength={200} autoFocus={isNew} onChange={(e) => set('name', e.target.value)} />
              </Field>
              <Field label="Formula Number">
                <input className="input" value={header.number} maxLength={100} onChange={(e) => set('number', e.target.value)} />
              </Field>
              <Field label="Customer Name">
                <input className="input" value={header.customerName} maxLength={200} onChange={(e) => set('customerName', e.target.value)} />
              </Field>
              <Field label="Category" hint={categoriesQ.data && categoryOptions.length === 0 ? 'No Formula categories in this group yet (Material Categories → type Formula).' : undefined}>
                <SearchSelect options={categoryOptions} value={header.categoryId} onChange={(v) => set('categoryId', v)} placeholder={categoriesQ.isLoading ? 'Loading…' : 'Select category'} />
              </Field>
              <Field label="Batch Size (g)" error={showErr('batchSize')}>
                <NumInput value={header.batchSize} onChange={(v) => set('batchSize', v)} suffix="g" invalid={!!showErr('batchSize')} placeholder="e.g. 1000" />
              </Field>
              <Field label="Substrate">
                <input className="input" value={header.substrate} maxLength={200} placeholder="e.g. Red oak" onChange={(e) => set('substrate', e.target.value)} />
              </Field>
              <Field label="Container Type">
                <input className="input" value={header.containerType} maxLength={200} placeholder="e.g. Quart can" onChange={(e) => set('containerType', e.target.value)} />
              </Field>
              <Field label="Container price ($)" error={showErr('containerPrice')}>
                <NumInput value={header.containerPrice} onChange={(v) => set('containerPrice', v)} prefix="$" invalid={!!showErr('containerPrice')} />
              </Field>
              <Field label="Mark-up (%)" error={showErr('markUp')}>
                <NumInput value={header.markUp} onChange={(v) => set('markUp', v)} suffix="%" invalid={!!showErr('markUp')} />
              </Field>
              <Field label="Status" error={showErr('isComplete')} hint="Formulas stay Incomplete until you mark them Complete." className="sm:col-span-2 xl:col-span-3">
                <Checkbox checked={header.isComplete} onChange={(v) => set('isComplete', v)} label="Complete — this formulation is finished and approved" />
              </Field>
              <Field label="Notes" className="sm:col-span-2 xl:col-span-3">
                <textarea className="input" rows={3} maxLength={4000} value={header.notes} onChange={(e) => set('notes', e.target.value)} placeholder="Mixing instructions, application notes…" />
              </Field>
            </div>
          </Card>

          {/* Colour */}
          <Card title="Colour match">
            <div className="space-y-4">
              {(['spin', 'spex'] as const).map((p) => {
                const deKey = `${p}DeltaE` as DeltaKey
                const match = deltaEMatch(parseNum(header[deKey]))
                return (
                  <div key={p}>
                    <div className="mb-1.5 flex items-center gap-2">
                      <span className="text-sm font-semibold">{p === 'spin' ? 'Spin' : 'Spex'}</span>
                      <span className="text-xs text-muted-foreground">{p === 'spin' ? 'Specular included' : 'Specular excluded'}</span>
                      {match && <span className={clsx('badge ml-auto', match.className)} title="Delta E">ΔE {header[deKey]} · {match.label}</span>}
                    </div>
                    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                      {(['L', 'A', 'B', 'E'] as const).map((c) => {
                        const k = `${p}Delta${c}` as DeltaKey
                        return (
                          <Field key={k} label={`Δ${c === 'L' || c === 'E' ? c : c.toLowerCase()}`} error={showErr(k)}>
                            <NumInput value={header[k]} onChange={(v) => set(k, v)} invalid={!!showErr(k)} placeholder="—" ariaLabel={`${p} delta ${c}`} />
                          </Field>
                        )
                      })}
                    </div>
                  </div>
                )
              })}
              <p className="text-xs text-muted-foreground">ΔE ≤ 1 excellent match · ≤ 2 commercial match · above 2 review the formula.</p>
            </div>
          </Card>

          {/* Ingredients */}
          <Card
            title={
              <span>
                Ingredients <span className="badge ml-1 bg-muted text-muted-foreground">{lines.length}</span>
              </span>
            }
            actions={
              <button className="btn-secondary btn-sm" onClick={() => setScaleOpen(true)} disabled={totals.totalGrams <= 0}>
                <Scale className="h-3.5 w-3.5" /> Scale batch
              </button>
            }
            bodyClassName="p-0"
          >
            <div className="border-b p-3">
              <SearchSelect
                options={materialOptions}
                value={null}
                onChange={addMaterial}
                placeholder={materialsQ.isLoading ? 'Loading materials…' : '+ Add to formula — search Base, Pigment, Dye or Product materials'}
                disabled={materialsQ.isLoading || gid <= 0}
                emptyText={materialsQ.data ? 'No matching materials (already added materials are hidden)' : 'No results found'}
              />
              {materialsQ.isError && <div className="mt-2 text-xs text-destructive">{errorMessage(materialsQ.error)}</div>}
            </div>
            {batchMismatch && (
              <div className="border-b p-3">
                <Note>
                  Ingredients add up to <b>{num(totals.totalGrams, 2)} g</b> but the batch size is <b>{num(batchSize, 2)} g</b>.{' '}
                  <button className="font-semibold underline" onClick={() => applyScale(batchSize, false)}>
                    Scale ingredients to the batch size
                  </button>
                </Note>
              </div>
            )}
            {lines.length === 0 ? (
              <EmptyState title="No ingredients yet" description="Use “Add to formula” to pick Base, Pigment, Dye or Product materials, then enter grams." icon={<FlaskConical className="h-5 w-5" />} />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="border-b bg-muted/60">
                    <tr>
                      <th className="th w-16">Order</th>
                      <th className="th">Material</th>
                      <th className="th hidden md:table-cell">Type</th>
                      <th className="th text-right">Grams</th>
                      <th className="th text-right">% of batch</th>
                      <th className="th hidden text-right md:table-cell">lb/gal</th>
                      <th className="th hidden text-right lg:table-cell">Gallons</th>
                      <th className="th hidden text-right md:table-cell">$/gal</th>
                      <th className="th text-right">Cost</th>
                      <th className="th w-10" />
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((l, i) => {
                      const g = gramsOf(l)
                      const gal = gallonsOf(g, l.density)
                      const err = showErr(`line-${l.key}`)
                      return (
                        <tr key={l.key} className="border-b last:border-0 hover:bg-muted/30">
                          <td className="td">
                            <div className="flex items-center gap-0.5">
                              <span className="w-5 text-xs tabular-nums text-muted-foreground">{i + 1}</span>
                              <button className="btn-icon h-6 w-6" title="Move up" disabled={i === 0} onClick={() => moveLine(i, -1)}>
                                <ArrowUp className="h-3.5 w-3.5" />
                              </button>
                              <button className="btn-icon h-6 w-6" title="Move down" disabled={i === lines.length - 1} onClick={() => moveLine(i, 1)}>
                                <ArrowDown className="h-3.5 w-3.5" />
                              </button>
                            </div>
                          </td>
                          <td className="td min-w-[10rem]">
                            <div className="font-medium">{l.productName}</div>
                            <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                              {l.productCode && <span>{l.productCode}</span>}
                              <span className="md:hidden">{l.materialTypeLabel}</span>
                              {l.materialDeleted && <span className="badge bg-destructive/10 text-destructive">deleted material</span>}
                              {l.density <= 0 && (
                                <span className="inline-flex items-center gap-1 text-amber-700 dark:text-amber-400" title="No density — gallons and cost cannot be calculated for this ingredient.">
                                  <AlertTriangle className="h-3 w-3" /> no density
                                </span>
                              )}
                            </div>
                          </td>
                          <td className="td hidden md:table-cell">{l.materialTypeLabel}</td>
                          <td className="td text-right">
                            <NumInput
                              className="ml-auto w-28"
                              value={l.grams}
                              onChange={(v) => updateGrams(l.key, v)}
                              suffix="g"
                              invalid={!!err}
                              autoFocus={l.key === lastAdded}
                              ariaLabel={`Grams of ${l.productName}`}
                            />
                            {err && <div className="mt-0.5 text-[11px] text-destructive">{err}</div>}
                          </td>
                          <td className="td text-right tabular-nums">{totals.totalGrams > 0 ? `${num((g / totals.totalGrams) * 100, 2)}%` : '—'}</td>
                          <td className="td hidden text-right tabular-nums md:table-cell">{num(l.density, 3)}</td>
                          <td className="td hidden text-right tabular-nums lg:table-cell">{num(gal, 4)}</td>
                          <td className="td hidden text-right tabular-nums md:table-cell">{money(l.price)}</td>
                          <td className="td text-right tabular-nums">{money(gal * l.price)}</td>
                          <td className="td">
                            <button className="btn-icon hover:text-destructive" title="Remove from formula" onClick={() => removeLine(l.key)}>
                              <Trash2 className="h-4 w-4" />
                            </button>
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="border-t-2 bg-muted/40 font-semibold">
                    <tr>
                      <td className="td" colSpan={2}>
                        Total
                      </td>
                      <td className="td hidden md:table-cell" />
                      <td className="td text-right tabular-nums">{num(totals.totalGrams, 2)} g</td>
                      <td className="td text-right tabular-nums">{totals.totalGrams > 0 ? '100%' : '—'}</td>
                      <td className="td hidden md:table-cell" />
                      <td className="td hidden text-right tabular-nums lg:table-cell">{num(totals.totalGallons, 4)}</td>
                      <td className="td hidden md:table-cell" />
                      <td className="td text-right tabular-nums">{money(totals.materialCost)}</td>
                      <td className="td" />
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
          </Card>
        </div>

        {/* Summary */}
        <aside className="space-y-5 lg:sticky lg:top-4 lg:self-start">
          <Card title="Summary" bodyClassName="p-4 pt-2">
            <div className="divide-y">
              <SummaryRow label="Total weight" value={`${num(totals.totalGrams, 2)} g`} hint={`${num(totals.totalPounds, 3)} lb`} />
              <SummaryRow label="Total gallons" value={num(totals.totalGallons, 4)} />
              <SummaryRow label="Materials cost" value={money(totals.materialCost)} />
              <SummaryRow label={`Mark-up (${num(markUp, 2)}%)`} value={money(totals.markUpAmount)} />
              <SummaryRow label="Container" value={money(containerPrice)} />
            </div>
            <div className="mt-2 rounded-md bg-accent px-3 py-2.5">
              <div className="text-xs font-medium text-accent-foreground">Formula Price</div>
              <div className="text-2xl font-semibold text-primary">{money(totals.price)}</div>
              <div className="text-xs text-muted-foreground">cost × (1 + mark-up) + container</div>
            </div>
            <div className="mt-2 divide-y">
              <SummaryRow label="Cost per gallon" value={money(totals.costPerGallon)} />
              <SummaryRow label="Price per gallon" value={money(totals.pricePerGallon)} />
            </div>
            <div className="mt-3 grid grid-cols-3 gap-2 text-center">
              {(['voc', 'hap', 'tap'] as const).map((k) => (
                <div key={k} className="rounded-md border px-2 py-2">
                  <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">{k}</div>
                  <div className="text-sm font-semibold tabular-nums">{num(totals[k], 3)}</div>
                  <div className="text-[10px] text-muted-foreground">lb/gal</div>
                </div>
              ))}
            </div>
          </Card>
          <Card title="Composition by weight">
            <CompositionDonut items={lines.map((l) => ({ key: l.key, name: l.productName, grams: gramsOf(l) }))} />
          </Card>
        </aside>
      </div>

      <ScaleBatchModal open={scaleOpen} onClose={() => setScaleOpen(false)} currentTotal={totals.totalGrams} batchSize={batchSize} onApply={applyScale} />
      {!isNew && detail.data && (
        <EntityDocuments open={docsOpen} onClose={() => setDocsOpen(false)} entityType="Formula" entityId={detail.data.id} groupId={detail.data.groupId} title={header.name || detail.data.name} />
      )}
    </>
  )
}

function ScaleBatchModal({ open, onClose, currentTotal, batchSize, onApply }: {
  open: boolean
  onClose: () => void
  currentTotal: number
  batchSize: number
  onApply: (target: number, setBatch: boolean) => void
}) {
  const [target, setTarget] = useState('')
  const [setBatch, setSetBatch] = useState(true)
  useEffect(() => {
    if (open) {
      setTarget(String(Math.round((batchSize > 0 ? batchSize : currentTotal) * 100) / 100))
      setSetBatch(true)
    }
  }, [open, batchSize, currentTotal])
  const value = parseNum(target)
  const valid = value !== null && value > 0
  const factor = valid && currentTotal > 0 ? value / currentTotal : null
  const apply = () => {
    if (!valid) return
    onApply(value, setBatch)
    onClose()
  }
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Scale batch"
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" disabled={!valid} onClick={apply}>
            <Scale className="h-4 w-4" /> Scale
          </button>
        </>
      }
    >
      <p className="mb-3 text-sm text-muted-foreground">
        Ingredients currently add up to <b className="text-foreground">{num(currentTotal, 2)} g</b>. Every ingredient is rescaled proportionally so the
        percentages stay the same.
      </p>
      <Field label="New batch size (g)" required error={target && !valid ? 'Enter a weight greater than 0.' : undefined} hint={factor ? `Scale factor × ${num(factor, 4)}` : undefined}>
        <NumInput value={target} onChange={setTarget} suffix="g" invalid={!!target && !valid} autoFocus />
      </Field>
      <div className="mt-3">
        <Checkbox checked={setBatch} onChange={setSetBatch} label="Also set the formula's Batch Size to this value" />
      </div>
    </Modal>
  )
}
