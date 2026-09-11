import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { AxiosError } from 'axios'
import {
  AlertTriangle, ArrowDown, ArrowLeft, ArrowUp, Calculator, Droplets, Eye, FileSpreadsheet, FileText, FlaskConical, History, Lock, MinusCircle,
  Plus, PlusCircle, Printer, RefreshCw, RotateCcw, Save, Scale, Send, Tag, Trash2, Undo2, XSquare,
} from 'lucide-react'
import clsx from 'clsx'
import { api, download, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { hasRole, isAdmin, Roles } from '@/lib/access'
import { dateTime, money, num } from '@/lib/format'
import { Card, ConfirmDialog, EmptyState, ErrorBanner, Field, LoadingBlock, Note, PageHeader, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { EntityDocuments } from '@/components/EntityDocuments'
import { useToast } from '@/components/toast'
import { CompositionDonut } from './CompositionDonut'
import { PrintDeniedModal, StatusBadge } from './FormulaModals'
import { FormulaPrintView, type PrintData, type PrintMode } from './FormulaPrintView'
import { DispensingModal, FormulaHistoryModal, NozzleConfirmModal, RecalcAmountModal, SelectLocationModal, type DispenseMaterial } from './WorkspaceModals'
import { readScaleWeight, tareScale, waitForCommand } from './deviceCommands'
import {
  batchScaleFactor, batchValueOf, computeTotals, deltaEMatch, flOzOf, gallonsOf, isWeightBatchType, parseNum, scaleByFactor, typeLetter, typeOrder,
} from './formulaMath'
import {
  BATCH_TYPES, COMPLETE_LOCKED, CONTAINER_TYPES, FORMULA_CATEGORY_TYPES, INGREDIENT_TYPES, type CategoryOption, type FormulaDetail, type InventoryBatch, type MaterialOption,
  type Workspace,
} from './types'

const DELTA_KEYS = ['spexDeltaL', 'spexDeltaA', 'spexDeltaB', 'spexDeltaE', 'spinDeltaL', 'spinDeltaA', 'spinDeltaB', 'spinDeltaE'] as const
type DeltaKey = (typeof DELTA_KEYS)[number]
const DISPENSED_FIRST = 'Please Record & Reset or revert your dispense operation.'
const COMPLETE_FIRST = 'Please complete 2 step formula save operation before modifying batch size/type.'
const SAVE_FIRST = 'Please save your changes first.'
/** Equipment & Materials tab of a material type (the "Not Enough Material in Batch" link). */
const MATERIAL_TAB: Record<number, string> = { 1: 'base', 2: 'pigment', 3: 'dye' }

type SaveOptions = {
  /** true = Formula Information "Save" (legacy step 2: the formula becomes Complete); false = "Mark as Incomplete"; omitted = keep the status. */
  complete?: boolean
  /** Batch-size rescale: Amount to Dispense = the new grams. */
  reset?: boolean
}

type HeaderState = {
  name: string
  number: string
  customerName: string
  categoryId: number | null
  batchType: number
  containerType: string
  containerPrice: string
  markUp: string
  substrate: string
  notes: string
  employeeName: string
  purchaseOrderNumber: string
  isComplete: boolean
} & Record<DeltaKey, string>

interface Line {
  key: string
  /** Saved ingredient id (undefined until the formula is saved with it). */
  id?: number
  materialId: number
  productName: string
  productCode?: string | null
  materialType: number
  materialTypeLabel: string
  categoryName?: string | null
  materialDeleted: boolean
  density: number
  price: number
  voc: number
  hap: number
  tap: number
  minQuantity: number
  colorCode?: string | null
  grams: string
  /** Grams as last saved (0 for new rows) — the server adds grams changes to the Amount to Dispense. */
  savedGrams: number
  dispenseAmount: number
  dispensedGrams: number
  isDispensed: boolean
  batchNumber: string | null
}

const emptyHeader: HeaderState = {
  name: '', number: '', customerName: '', categoryId: null, batchType: 2, containerType: CONTAINER_TYPES[0], containerPrice: '0', markUp: '0',
  substrate: '', notes: '', employeeName: '', purchaseOrderNumber: '', isComplete: false,
  spinDeltaL: '', spinDeltaA: '', spinDeltaB: '', spinDeltaE: '', spexDeltaL: '', spexDeltaA: '', spexDeltaB: '', spexDeltaE: '',
}

const str = (v?: number | null) => (v === null || v === undefined ? '' : String(v))
let keySeq = 0
const nextKey = () => `l${++keySeq}`

function fromDetail(d: FormulaDetail): { header: HeaderState; lines: Line[] } {
  const header: HeaderState = {
    name: d.name, number: d.number ?? '', customerName: d.customerName ?? '', categoryId: d.categoryId ?? null, batchType: d.batchType || 2,
    containerType: d.containerType ?? '', containerPrice: str(d.containerPrice), markUp: str(d.markUp), substrate: d.substrate ?? '', notes: d.notes ?? '',
    employeeName: d.employeeName ?? '', purchaseOrderNumber: d.purchaseOrderNumber ?? '', isComplete: d.isComplete,
    spinDeltaL: str(d.spinDeltaL), spinDeltaA: str(d.spinDeltaA), spinDeltaB: str(d.spinDeltaB), spinDeltaE: str(d.spinDeltaE),
    spexDeltaL: str(d.spexDeltaL), spexDeltaA: str(d.spexDeltaA), spexDeltaB: str(d.spexDeltaB), spexDeltaE: str(d.spexDeltaE),
  }
  const lines = d.ingredients.map((i) => ({
    key: nextKey(), id: i.id, materialId: i.materialId, productName: i.productName, productCode: i.productCode, materialType: i.materialType,
    materialTypeLabel: i.materialTypeLabel, categoryName: i.categoryName, materialDeleted: i.materialDeleted, density: i.density, price: i.price,
    voc: i.voc, hap: i.hap, tap: i.tap, minQuantity: i.minQuantity, colorCode: i.colorCode, grams: str(i.grams), savedGrams: i.grams,
    dispenseAmount: i.dispenseAmount, dispensedGrams: i.dispensedGrams, isDispensed: i.isDispensed, batchNumber: i.batchNumber ?? null,
  }))
  return { header, lines }
}

const snapshot = (h: HeaderState, lines: Line[]) => JSON.stringify([h, lines.map((l) => [l.materialId, l.grams, l.id ? null : l.batchNumber])])
const badNumber = (s: string) => s.trim() !== '' && parseNum(s) === null

function validate(h: HeaderState, lines: Line[], requireNumber: boolean, requireEmployee: boolean) {
  const e: Record<string, string> = {}
  if (!h.name.trim()) e.name = 'Formula Name is required.'
  if (requireNumber && !h.number.trim()) e.number = 'Formula Number is required.'
  if (requireEmployee && h.isComplete && !h.employeeName.trim()) e.employeeName = 'Employee Name is required.'
  const nonNegative: [keyof HeaderState, string][] = [['containerPrice', 'Container price'], ['markUp', 'Mark-up']]
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
function NumInput({ value, onChange, prefix, suffix, invalid, placeholder, className, autoFocus, ariaLabel, onBlur, onEnter, readOnly }: {
  value: string
  onChange: (v: string) => void
  prefix?: string
  suffix?: string
  invalid?: boolean
  placeholder?: string
  className?: string
  autoFocus?: boolean
  ariaLabel?: string
  onBlur?: () => void
  onEnter?: () => void
  readOnly?: boolean
}) {
  return (
    <div className={clsx('relative', className)}>
      {prefix && <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">{prefix}</span>}
      <input
        className={clsx('input tabular-nums', prefix && 'pl-6', suffix && 'pr-9', invalid && 'input-invalid', readOnly && 'bg-muted/50')}
        inputMode="decimal"
        value={value}
        placeholder={placeholder}
        autoFocus={autoFocus}
        aria-label={ariaLabel}
        readOnly={readOnly}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        onKeyDown={(e) => e.key === 'Enter' && onEnter?.()}
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

function DeviceSelect({ icon, label, value, options, placeholder, onChange, busy }: {
  icon: ReactNode
  label: string
  value: number | null | undefined
  options: { id: number; name: string; bridgeName?: string | null; bridgeOnline?: boolean | null }[]
  placeholder: string
  onChange: (id: number | null) => void
  busy?: boolean
}) {
  const current = options.find((o) => o.id === value)
  return (
    <Field label={<span className="inline-flex items-center gap-1.5">{icon} {label}</span>} hint={current ? (current.bridgeName ? `via ${current.bridgeName}${current.bridgeOnline ? ' · online' : ' · offline'}` : 'not connected to a Network Bridge') : undefined}>
      <div className="flex items-center gap-2">
        <select className="input" value={value ?? ''} onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)} aria-label={label}>
          <option value="">{placeholder}</option>
          {options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </select>
        {busy && <Spinner />}
      </div>
    </Field>
  )
}

export default function FormulaEditPage() {
  const { id = 'new' } = useParams()
  const isNew = id === 'new'
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()
  const { groupId, group, groups } = useGroup()
  const me = useMe()
  const admin = isAdmin(me)
  // Legacy Edit.cshtml: Calc Batch for Admin, FGPro and FGProPlus; an FG Pro-only user cannot change a Complete formula's ingredients.
  const canCalcBatch = hasRole(me, Roles.SystemAdmin, Roles.FGPro, Roles.FGProPlus)
  const fgProOnly = me.roles.length > 0 && me.roles.every((r) => r === Roles.FGPro)

  const [header, setHeader] = useState<HeaderState>(emptyHeader)
  const [lines, setLines] = useState<Line[]>([])
  const [baseline, setBaseline] = useState(() => snapshot(emptyHeader, []))
  const [loadedAt, setLoadedAt] = useState(0)
  const [submitted, setSubmitted] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const [lastAdded, setLastAdded] = useState<string | null>(null)
  const [docsOpen, setDocsOpen] = useState(false)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [printing, setPrinting] = useState<PrintMode | null>(params.get('print') === '1' ? 'formula' : null)
  const [autoPrint, setAutoPrint] = useState(params.get('print') === '1')
  const [printDenied, setPrintDenied] = useState(false)
  const [autoSave, setAutoSave] = useState<SaveOptions | null>(null)
  const [completeAttempt, setCompleteAttempt] = useState(false)
  const [moveTo, setMoveTo] = useState<number | null>(null)
  // picker
  const [pickType, setPickType] = useState<number>(0)
  const [pickMaterial, setPickMaterial] = useState<number | null>(null)
  const [pickQty, setPickQty] = useState('')
  const [pickBatch, setPickBatch] = useState('')
  const [rowAdd, setRowAdd] = useState<Record<string, string>>({})
  // batch size / reweigh
  const [batchDraft, setBatchDraft] = useState<string | null>(null)
  const [reweigh, setReweigh] = useState('')
  // devices / dispensing
  const [scaleBusy, setScaleBusy] = useState<string | null>(null)
  const [nozzleOpen, setNozzleOpen] = useState(false)
  const [nozzleShown, setNozzleShown] = useState(false)
  const [dispenseCmd, setDispenseCmd] = useState<{ id: number; materials: DispenseMaterial[] } | null>(null)
  const [recalcLine, setRecalcLine] = useState<Line | null>(null)
  const [locationOpen, setLocationOpen] = useState(false)
  const [labelPrinter, setLabelPrinter] = useState<number | null>(null)

  const detail = useQuery({
    queryKey: ['formula', id],
    queryFn: () => api.get<FormulaDetail>(`/formulas/${id}`).then((r) => r.data),
    enabled: !isNew,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  })
  const workspace = useQuery({
    queryKey: ['formula-workspace', id],
    queryFn: () => api.get<Workspace>(`/formulas/${id}/workspace`).then((r) => r.data),
    enabled: !isNew,
    refetchOnWindowFocus: false,
  })
  const ws = workspace.data
  const savedComplete = !isNew && !!detail.data?.isComplete
  const locked = savedComplete && fgProOnly
  const lockTitle = locked ? COMPLETE_LOCKED : undefined

  // Populate the form whenever fresh server data arrives (first load, after save / workspace actions).
  useEffect(() => {
    if (isNew || !detail.data || detail.dataUpdatedAt === loadedAt) return
    const { header: h, lines: l } = fromDetail(detail.data)
    setHeader(h)
    setLines(l)
    setBaseline(snapshot(h, l))
    setLoadedAt(detail.dataUpdatedAt)
    setSubmitted(false)
    setCompleteAttempt(false)
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

  useEffect(() => setLabelPrinter(ws?.printerDeviceId ?? null), [ws?.printerDeviceId])

  const gid = isNew ? groupId : (detail.data?.groupId ?? 0)
  const groupName = isNew ? (group?.name ?? '') : (detail.data?.groupName ?? '')

  const materialsQ = useQuery({
    queryKey: ['materials', gid],
    queryFn: () => api.get<MaterialOption[]>('/materials', { params: { groupId: gid } }).then((r) => r.data),
    enabled: gid > 0,
  })
  const categoriesQ = useQuery({
    queryKey: ['material-categories', gid, 'formula'],
    queryFn: () =>
      api.get<CategoryOption[]>('/material-categories', { params: { groupId: gid } })
        .then((r) => r.data.filter((c) => c.materialType == null || FORMULA_CATEGORY_TYPES.includes(c.materialType))),
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
  const gramsOf = (l: Line) => Math.max(0, parseNum(l.grams) ?? 0)
  const totals = useMemo(
    () => computeTotals(lines.map((l) => ({ grams: gramsOf(l), density: l.density, price: l.price, voc: l.voc, hap: l.hap, tap: l.tap })), markUp, containerPrice),
    [lines, markUp, containerPrice],
  )
  const requireNumber = isNew || !!detail.data?.number
  const requireEmployee = isNew || !detail.data?.isComplete
  const errors = useMemo(
    () => validate({ ...header, isComplete: header.isComplete || completeAttempt }, lines, requireNumber, requireEmployee),
    [header, lines, requireNumber, requireEmployee, completeAttempt],
  )
  const showErr = (k: string) => (submitted ? errors[k] : undefined)

  const sums = useMemo(() => {
    const by = (t: number) => lines.filter((l) => l.materialType === t).reduce((s, l) => s + gramsOf(l), 0)
    return { base: by(1), pigment: by(2), dye: by(3) }
  }, [lines])
  const pct = (part: number) => (sums.base > 0 ? `${((part / sums.base) * 100).toFixed(2)}%` : '0.00%')

  // ---------------- batches / dispense helpers
  const usesBatches = ws?.usesBatches ?? detail.data?.usesBatches ?? false
  const batchesFor = (materialId: number): InventoryBatch[] => ws?.batches[String(materialId)] ?? []
  const projectedDispense = (l: Line) => (l.id ? Math.max(0, l.dispenseAmount + (gramsOf(l) - l.savedGrams)) : gramsOf(l))
  /** Latest batch (batches come in creation order) — the legacy default when no batch is saved. */
  const latestBatch = (materialId: number): InventoryBatch | null => {
    const list = batchesFor(materialId)
    return list.length ? list[list.length - 1] : null
  }
  const chosenBatch = (l: Line) => {
    const list = batchesFor(l.materialId)
    return l.batchNumber ? list.find((b) => b.batchNumber === l.batchNumber) ?? null : latestBatch(l.materialId)
  }
  const notEnough = (l: Line) => {
    const b = chosenBatch(l)
    if (!b) return false
    const gal = gallonsOf(projectedDispense(l), l.density)
    return gal > b.onHand || (l.minQuantity > 0 && b.onHand < l.minQuantity)
  }
  const anyDispensed = lines.some((l) => l.dispensedGrams > 0)
  const scaleId = ws?.scaleDeviceId ?? null
  const scale = ws?.scales.find((s) => s.id === scaleId)
  const dispenser = ws?.dispensers.find((d) => d.id === ws.dispenserId)
  const purgeFailures = (ws?.purgeFailures ?? []).filter((p) => p.bridgeDeviceId === dispenser?.bridgeId)

  // Clean-nozzle popup once per visit (legacy showCleanNozzlePopUp).
  useEffect(() => {
    if (ws?.nozzle.cleaningRequired && !nozzleShown && (ws.dispensers.length ?? 0) > 0) {
      setNozzleShown(true)
      setNozzleOpen(true)
    }
  }, [ws, nozzleShown])

  const refreshAll = () => {
    qc.invalidateQueries({ queryKey: ['formula', id] })
    qc.invalidateQueries({ queryKey: ['formula-workspace', id] })
    qc.invalidateQueries({ queryKey: ['formula-history', id] })
    qc.invalidateQueries({ queryKey: ['formulas'] })
  }

  // Re-attach to a dispense that is still running (page reload during a dispense).
  useEffect(() => {
    if (!ws?.openDispenseCommandId || dispenseCmd) return
    setDispenseCmd({ id: ws.openDispenseCommandId, materials: dispenseMaterials() })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ws?.openDispenseCommandId])

  function dispenseMaterials(): DispenseMaterial[] {
    const configured = new Set((dispenser?.canisters ?? []).map((c) => c.materialId))
    return lines
      .filter((l) => (l.materialType === 2 || l.materialType === 3) && l.dispenseAmount > 0 && configured.has(l.materialId))
      .map((l) => ({ key: l.key, productCode: l.productCode, productName: l.productName, grams: l.dispenseAmount, colorCode: l.colorCode }))
  }

  // ---------------- scale
  async function takeWeight(): Promise<number | null> {
    if (!scaleId) {
      toast.error('Please select a scale or manually enter weight.')
      return null
    }
    setScaleBusy('Reading the scale…')
    try {
      const w = await readScaleWeight(scaleId)
      tareScale(scaleId).then(() => toast.success('Scale successfully tared.')).catch((e: Error) => toast.error(e.message))
      return w
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Network bridge could not detect scale.')
      return null
    } finally {
      setScaleBusy(null)
    }
  }

  // ---------------- ingredients
  const inFormula = useMemo(() => new Set(lines.map((l) => l.materialId)), [lines])
  const materialOptions = useMemo(
    () =>
      (materialsQ.data ?? [])
        .filter((m) => INGREDIENT_TYPES.includes(m.materialType) && !inFormula.has(m.id) && (pickType === 0 || m.materialType === pickType))
        .sort((a, b) => a.productName.localeCompare(b.productName))
        .map((m) => ({
          value: m.id,
          label: m.productName,
          sub: [m.materialTypeLabel, m.categoryName, m.productCode, `${num(m.density, 3)} lb/gal`, `${money(m.price)}/gal`].filter(Boolean).join(' · '),
        })),
    [materialsQ.data, inFormula, pickType],
  )
  const pickedMaterial = materialsQ.data?.find((m) => m.id === pickMaterial)
  const pickBatches = pickedMaterial ? batchesFor(pickedMaterial.id) : []

  const addLine = (m: MaterialOption, grams: number, batchNumber: string | null) => {
    const key = nextKey()
    setLines((ls) => [
      ...ls,
      {
        key, materialId: m.id, productName: m.productName, productCode: m.productCode, materialType: m.materialType, materialTypeLabel: m.materialTypeLabel,
        categoryName: m.categoryName, materialDeleted: false, density: m.density, price: m.price, voc: m.voc, hap: m.hap, tap: m.tap,
        minQuantity: m.minQuantity, colorCode: null, grams: String(grams), savedGrams: 0, dispenseAmount: grams, dispensedGrams: 0, isDispensed: false,
        batchNumber,
      },
    ])
    setLastAdded(key)
  }

  async function addFromPicker() {
    const m = pickedMaterial
    if (!m) return toast.error('Please select a material to add.')
    let grams = parseNum(pickQty)
    if (pickQty.trim() && (grams === null || grams <= 0)) return toast.error(`Invalid Number entered [${pickQty}]`)
    const list = batchesFor(m.id)
    if (grams === null) {
      if (!scaleId) return toast.error('Please enter grams')
      grams = await takeWeight()
      if (grams === null) return
    }
    const batch = pickBatch ? list.find((b) => b.batchNumber === pickBatch) ?? null : latestBatch(m.id)
    if (batch && m.density > 0 && gallonsOf(grams, m.density) > batch.onHand) return toast.error('The selected batch does not have enough material.')
    addLine(m, grams, batch?.batchNumber ?? null)
    setPickMaterial(null)
    setPickQty('')
    setPickBatch('')
    toast.success('Material was successfully added to the color formula.')
    if (!isNew) setAutoSave({})
  }

  async function addToRow(l: Line) {
    let grams = parseNum(rowAdd[l.key] ?? '')
    if ((rowAdd[l.key] ?? '').trim() && (grams === null || grams <= 0)) return toast.error(`Invalid Number entered [${rowAdd[l.key]}]`)
    if (locked) return toast.error(COMPLETE_LOCKED)
    if (grams === null) grams = await takeWeight()
    if (grams === null) return
    const add = grams
    setLines((ls) => ls.map((x) => (x.key === l.key ? { ...x, grams: String(Math.round((gramsOf(x) + add) * 10000) / 10000) } : x)))
    setRowAdd((r) => ({ ...r, [l.key]: '' }))
    toast.success('Material was successfully added to the color formula.')
    if (!isNew) setAutoSave({})
  }

  const updateGrams = (key: string, grams: string) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, grams } : l)))
  const removeLine = (key: string) => {
    setLines((ls) => ls.filter((l) => l.key !== key))
    toast.success('Material was successfully removed from the color formula.')
    if (!isNew) setAutoSave({})
  }
  const moveLine = (index: number, dir: -1 | 1) =>
    setLines((ls) => {
      const j = index + dir
      if (j < 0 || j >= ls.length) return ls
      const next = [...ls]
      ;[next[index], next[j]] = [next[j], next[index]]
      return next
    })

  // ---------------- batch size (value in the selected Batch Type unit; changing it rescales every ingredient)
  const batchValue = batchValueOf(header.batchType, totals.totalGrams, totals.totalGallons)
  const batchUnit = BATCH_TYPES.find((b) => b.value === header.batchType)?.unit ?? 'g'
  const missingDensity = lines.filter((l) => l.density <= 0)
  // Legacy formulaInputLogic: batch size / type cannot change while something is dispensed or before the formula is Complete.
  const batchBlockReason = anyDispensed ? DISPENSED_FIRST : !isNew && !savedComplete ? COMPLETE_FIRST : null
  const applyBatchSize = () => {
    if (batchDraft === null) return
    const v = parseNum(batchDraft)
    setBatchDraft(null)
    if (v === null || Math.abs(v - batchValue) < 0.00005) return
    if (batchBlockReason) return toast.error(batchBlockReason)
    if (locked) return toast.error(COMPLETE_LOCKED)
    if (!isWeightBatchType(header.batchType) && missingDensity.length > 0)
      return toast.error(`No density for ${missingDensity.map((l) => l.productName).join(', ')} — a volume batch size cannot be calculated.`)
    const factor = batchScaleFactor(header.batchType, v, totals.totalGrams, totals.totalGallons)
    if (!factor) return toast.error('Enter a batch size greater than 0.')
    const scaled = scaleByFactor(lines.map(gramsOf), factor)
    setLines((ls) => ls.map((l, i) => ({ ...l, grams: String(scaled[i]) })))
    toast.success(`Batch set to ${num(v, 4)} ${batchUnit}.`)
    // Legacy: the rescaled formula is saved at once and every changed row's Amount to Dispense becomes its new grams.
    if (!isNew) setAutoSave({ reset: true })
  }
  const changeBatchType = (t: number) => {
    if (t === header.batchType) return
    if (batchBlockReason) return toast.error(batchBlockReason)
    set('batchType', t)
    if (!isNew) setAutoSave({})
  }

  // ---------------- save
  const save = useMutation({
    mutationFn: async (opts: SaveOptions) => {
      const body = {
        groupId: gid,
        categoryId: header.categoryId,
        name: header.name.trim(),
        number: header.number.trim() || null,
        customerName: header.customerName.trim() || null,
        isComplete: opts.complete ?? header.isComplete,
        resetDispenseAmounts: !!opts.reset,
        batchType: header.batchType,
        containerType: header.containerType.trim() || null,
        containerPrice,
        markUp,
        substrate: header.substrate.trim() || null,
        notes: header.notes.trim() || null,
        employeeName: header.employeeName.trim() || null,
        purchaseOrderNumber: header.purchaseOrderNumber.trim() || null,
        ...Object.fromEntries(DELTA_KEYS.map((k) => [k, parseNum(header[k])])),
        ingredients: lines.map((l, i) => ({ materialId: l.materialId, grams: parseNum(l.grams) ?? 0, sequence: i + 1, batchNumber: l.id ? null : l.batchNumber })),
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
      else refreshAll()
    },
    onError: (e, opts) => {
      setServerError(errorMessage(e))
      document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' })
      // A refused batch-size rescale must not leave the rescaled (unsaved) grams behind: reload the saved formula.
      if (!isNew && opts.reset) refreshAll()
    },
  })

  /** Validates and saves; returns false when the form has errors. */
  const submit = (opts: SaveOptions = {}) => {
    setSubmitted(true)
    if (opts.complete) setCompleteAttempt(true)
    const errs = validate({ ...header, isComplete: opts.complete ?? header.isComplete }, lines, requireNumber, requireEmployee)
    if (Object.keys(errs).length > 0) {
      setServerError('Please fix the highlighted fields.')
      return false
    }
    save.mutate(opts)
    return true
  }

  // Legacy saved the formula after every material add / remove, batch size and batch type change.
  useEffect(() => {
    if (!autoSave) return
    const opts = autoSave
    setAutoSave(null)
    submit(opts)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoSave])

  const goBack = () => {
    if (dirty && !window.confirm('You have unsaved changes. Leave without saving?')) return
    navigate('/formulas')
  }

  // ---------------- workspace actions (server-side; the formula must be saved first)
  const onError = (e: unknown) => toast.error(errorMessage(e))
  const ok = (res: { data: { message: string } }) => {
    toast.success(res.data.message)
    refreshAll()
  }
  const setDevice = useMutation({
    mutationFn: ({ kind, deviceId }: { kind: 'scale' | 'printer' | 'dispenser'; deviceId: number | null }) =>
      api.put<{ message: string }>(kind === 'dispenser' ? `/formulas/${id}/dispenser` : `/formulas/${id}/preferences/${kind}`, { deviceId }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formula-workspace', id] })
    },
    onError,
  })
  const setBatch = useMutation({
    mutationFn: ({ line, batchNumber }: { line: Line; batchNumber: string | null }) =>
      api.put<{ message: string }>(`/formulas/${id}/ingredients/${line.id}/batch`, { batchNumber }),
    onSuccess: (res, v) => {
      toast.success(res.data.message)
      setLines((ls) => ls.map((l) => (l.key === v.line.key ? { ...l, batchNumber: v.batchNumber } : l)))
      qc.invalidateQueries({ queryKey: ['formula-history', id] })
    },
    onError,
  })

  // Legacy default: a material with several batches and none saved uses its latest batch; the choice is saved on load.
  const defaulted = useRef('')
  useEffect(() => {
    if (isNew || !ws || !usesBatches) return
    const todo = lines.filter((l) => l.id && !l.batchNumber && batchesFor(l.materialId).length > 1)
    const sig = `${loadedAt}:${todo.map((l) => l.id).join(',')}`
    if (todo.length === 0 || defaulted.current === sig) return
    defaulted.current = sig
    const picks = new Map(todo.map((l) => [l.key, latestBatch(l.materialId)!.batchNumber]))
    Promise.all(todo.map((l) => api.put(`/formulas/${id}/ingredients/${l.id}/batch`, { batchNumber: picks.get(l.key) })))
      .then(() => {
        setLines((ls) => ls.map((l) => (picks.has(l.key) && !l.batchNumber ? { ...l, batchNumber: picks.get(l.key)! } : l)))
        qc.invalidateQueries({ queryKey: ['formula-history', id] })
      })
      .catch(() => undefined)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ws, lines, loadedAt, usesBatches, isNew])

  const moveGroup = useMutation({
    mutationFn: (target: number) => api.put<{ message: string }>(`/formulas/${id}/group`, { groupId: target }),
    onSuccess: (res) => {
      setMoveTo(null)
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['documents'] })
      refreshAll()
    },
    onError: (e) => {
      setMoveTo(null)
      toast.error(errorMessage(e))
    },
  })
  const rowAction = useMutation({
    mutationFn: ({ line, action }: { line: Line; action: 'mark-dispensed' | 'revert-dispensed' | 'reset-dispense' }) =>
      api.post<{ message: string }>(`/formulas/${id}/ingredients/${line.id}/${action}`),
    onSuccess: ok,
    onError,
  })
  const recalc = useMutation({
    mutationFn: ({ line, grams }: { line: Line; grams: number }) => api.post<{ message: string }>(`/formulas/${id}/recalc`, { ingredientId: line.id, grams }),
    onSuccess: (res) => {
      setRecalcLine(null)
      ok(res)
    },
    onError,
  })
  const reweighM = useMutation({
    mutationFn: (totalGrams: number) => api.post<{ message: string }>(`/formulas/${id}/reweigh`, { totalGrams }),
    onSuccess: (res) => {
      setReweigh('')
      ok(res)
    },
    onError,
  })
  const record = useMutation({
    mutationFn: (locationId: number | null) => api.post<{ message: string; warning?: string | null }>(`/formulas/${id}/record`, { locationId }),
    onSuccess: (res) => {
      setLocationOpen(false)
      if (res.data.warning) toast.error(res.data.warning)
      ok(res)
    },
    onError,
  })
  const undo = useMutation({ mutationFn: () => api.post<{ message: string }>(`/formulas/${id}/undo-dispense`), onSuccess: ok, onError })
  const dispense = useMutation({
    mutationFn: () => api.post<{ message: string; commandId: number }>(`/formulas/${id}/dispense`, { deviceId: ws?.dispenserId }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDispenseCmd({ id: res.data.commandId, materials: dispenseMaterials() })
    },
    onError: (e) => {
      if ((e as AxiosError).response?.status === 409) setNozzleOpen(true)
      else toast.error(errorMessage(e))
    },
  })
  const sendLabel = useMutation({
    mutationFn: ({ customerLabel, printerDeviceId }: { customerLabel: boolean; printerDeviceId: number | null }) =>
      api.post<{ message: string; commandId: number }>(`/formulas/${id}/print-label`, { printerDeviceId, customerLabel }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formula-workspace', id] })
      waitForCommand(res.data.commandId, { timeoutMs: 60_000, intervalMs: 2000 })
        .then((s) => (s.status === 'Succeeded' ? toast.success('Label printed.') : toast.error(s.message ?? 'Label printing failed.')))
        .catch(() => undefined)
    },
    onError,
  })

  const needSaved = (): boolean => {
    if (isNew) {
      toast.error('Save the formula first.')
      return false
    }
    if (dirty) {
      toast.error(SAVE_FIRST)
      return false
    }
    return true
  }

  /** Saves pending edits first (legacy saveFormulaAjax before printing); false when the form cannot be saved. */
  const ensureSaved = async (): Promise<boolean> => {
    if (isNew) {
      toast.error('Save the formula first.')
      return false
    }
    if (!dirty) return true
    setSubmitted(true)
    if (Object.keys(validate(header, lines, requireNumber, requireEmployee)).length > 0) {
      setServerError('Please fix the highlighted fields.')
      return false
    }
    try {
      await save.mutateAsync({})
      return true
    } catch {
      return false
    }
  }

  // One-click can labels (legacy printLabel / PrintCanLabel): straight to the network label printer; without any label
  // printer in the group the browser prints the label instead.
  const hasPrinters = (ws?.printers.length ?? 0) > 0
  const printFormulaLabel = async () => {
    if (!hasPrinters) return openPrint('label')
    if (!ws?.printerDeviceId) return toast.error('Please Select a Printer!')
    if (await ensureSaved()) sendLabel.mutate({ customerLabel: false, printerDeviceId: ws.printerDeviceId })
  }
  const printCustomerLabel = async () => {
    if (!header.isComplete) return setPrintDenied(true)
    if (!hasPrinters) return openPrint('customerLabel')
    if (!ws?.printerDeviceId) return toast.error('Printer is not selected')
    if (await ensureSaved()) sendLabel.mutate({ customerLabel: true, printerDeviceId: null })
  }

  const onDispense = () => {
    if (!needSaved() || !ws) return
    if (ws.nozzle.cleaningRequired) return setNozzleOpen(true)
    if (!ws.dispenserId) return toast.error('Please select a dispenser device to continue.')
    if (dispenser && !dispenser.bridgeOnline) return toast.error(`The network bridge of ${dispenser.name} is offline.`)
    if (lines.some((l) => notEnough(l))) return toast.error('Add Material to Batch')
    dispense.mutate()
  }

  const onRecord = () => {
    if (!needSaved() || !ws) return
    if (!lines.some((l) => l.dispensedGrams > 0)) return toast.error('No materials have Dispensed Amounts.')
    if (ws.locations.length > 0) setLocationOpen(true)
    else record.mutate(null)
  }

  async function onRecalc(l: Line) {
    if (!needSaved()) return
    if (locked) return toast.error(COMPLETE_LOCKED)
    if (anyDispensed) return toast.error(DISPENSED_FIRST)
    if (scaleId) {
      const w = await takeWeight()
      if (w !== null) recalc.mutate({ line: l, grams: w })
    } else {
      toast.success('No scale selected, please enter gram amount you would like to recalc with.')
      setRecalcLine(l)
    }
  }

  const onReweigh = () => {
    if (!needSaved()) return
    if (locked) return toast.error(COMPLETE_LOCKED)
    if (anyDispensed) return toast.error(DISPENSED_FIRST)
    const v = parseNum(reweigh)
    if (v === null || v <= 0) return toast.error('No valid weight entered for batch')
    reweighM.mutate(v)
  }

  const readReweighFromScale = async () => {
    const w = await takeWeight()
    if (w !== null) {
      setReweigh(String(w))
      toast.success('Weight successfully acquired from scale.')
    }
  }

  // ---------------- print
  const printData: PrintData = {
    groupName,
    groupLogoFile: detail.data?.groupLogoFile,
    name: header.name,
    number: header.number,
    employeeName: header.employeeName,
    categoryName: categoriesQ.data?.find((c) => c.id === header.categoryId)?.name ?? detail.data?.categoryName,
    mixedOn: detail.data?.mixedOn,
    createdOn: detail.data?.createdAt,
    substrate: header.substrate,
    notes: header.notes,
    customerName: header.customerName,
    purchaseOrderNumber: header.purchaseOrderNumber,
    markUp,
    containerPrice,
    containerType: header.containerType,
    batchNumbers: [...new Set(lines.filter((l) => l.materialType === 1 && l.batchNumber && l.batchNumber !== '0').map((l) => l.batchNumber!))],
    spex: { l: parseNum(header.spexDeltaL), a: parseNum(header.spexDeltaA), b: parseNum(header.spexDeltaB), e: parseNum(header.spexDeltaE) },
    spin: { l: parseNum(header.spinDeltaL), a: parseNum(header.spinDeltaA), b: parseNum(header.spinDeltaB), e: parseNum(header.spinDeltaE) },
    totals,
    lines: [...lines]
      .map((l, i) => ({ l, i }))
      .sort((a, b) => typeOrder(a.l.materialType) - typeOrder(b.l.materialType) || a.i - b.i)
      .map(({ l }) => ({ key: l.key, type: l.materialTypeLabel, name: l.productName, number: l.productCode, grams: gramsOf(l), flOz: flOzOf(gramsOf(l), l.density) })),
  }
  const printReady = isNew || loadedAt > 0
  useEffect(() => {
    if (!printing || !autoPrint || !printReady) return
    setAutoPrint(false)
    if (printing === 'formula' && !header.isComplete) {
      setPrinting(null)
      setPrintDenied(true)
      return
    }
    const t = setTimeout(() => window.print(), 250)
    return () => clearTimeout(t)
  }, [printing, autoPrint, printReady, header.isComplete])

  const openPrint = (mode: PrintMode) => {
    if (mode !== 'label' && !header.isComplete) return setPrintDenied(true)
    setPrinting(mode)
  }

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
        mode={printing}
        onPrint={() => window.print()}
        onBack={() => (params.get('print') === '1' ? navigate('/formulas') : setPrinting(null))}
        onEdit={
          params.get('print') === '1'
            ? () => {
                setParams({}, { replace: true })
                setPrinting(null)
              }
            : undefined
        }
        toolbar={
          printing !== 'formula' && !isNew && (ws?.printers.length ?? 0) > 0 ? (
            <div className="flex flex-wrap items-center gap-2">
              <select className="input h-9 w-56" value={labelPrinter ?? ''} onChange={(e) => setLabelPrinter(e.target.value ? Number(e.target.value) : null)} aria-label="Label Printer">
                <option value="">Please select a Label Printer...</option>
                {ws?.printers.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
              <button
                className="btn-secondary"
                disabled={sendLabel.isPending || dirty}
                title={dirty ? 'Save your changes first' : 'Send the label to the network label printer'}
                onClick={() =>
                  labelPrinter || printing === 'customerLabel'
                    ? sendLabel.mutate({ customerLabel: printing === 'customerLabel', printerDeviceId: labelPrinter })
                    : toast.error('Please Select a Printer!')
                }
              >
                {sendLabel.isPending ? <Spinner /> : <Send className="h-4 w-4" />} Send to label printer
              </button>
            </div>
          ) : undefined
        }
      />
    )

  const categoryOptions = (categoriesQ.data ?? []).map((c) => ({ value: c.id, label: c.name }))
  // Keep the formula's current category selectable even when it is of another type (imported legacy data).
  if (header.categoryId && !categoryOptions.some((o) => o.value === header.categoryId) && detail.data?.categoryName)
    categoryOptions.push({ value: header.categoryId, label: detail.data.categoryName })
  const containerOptions = header.containerType && !CONTAINER_TYPES.includes(header.containerType) ? [header.containerType, ...CONTAINER_TYPES] : CONTAINER_TYPES
  const title = isNew ? 'New Formulation' : `Edit Formula: ${header.name || detail.data?.name || ''}${header.number ? ` - ${header.number}` : ''}`
  const busyWorkspace = rowAction.isPending || recalc.isPending || reweighM.isPending || record.isPending || undo.isPending || dispense.isPending
  const showAddColumn = !!scaleId
  const showCalc = !isNew && canCalcBatch
  // Machine dispensing needs a dispense machine whose network bridge is online.
  const canMachineDispense = (ws?.dispensers ?? []).some((d) => d.bridgeOnline)
  const groupOptions = detail.data && !groups.some((g) => g.id === detail.data!.groupId) ? [{ id: detail.data.groupId, name: detail.data.groupName }, ...groups] : groups
  const hasDevices = !!ws && (ws.scales.length > 0 || ws.dispensers.length > 0 || ws.printers.length > 0)

  return (
    <>
      <PageHeader
        title={title}
        breadcrumbs={[['Formulas', '/formulas'], isNew ? 'New Formulation' : header.name || 'Formula']}
        subtitle={
          <span className="inline-flex flex-wrap items-center gap-2">
            <span>Formulation workspace · {groupName}</span>
            <StatusBadge complete={header.isComplete} />
            {dirty && <span className="badge bg-muted text-muted-foreground">Unsaved changes</span>}
            {!isNew && detail.data?.mixedOn && <span className="text-xs">Mixed on {dateTime(detail.data.mixedOn)}</span>}
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
            {!isNew && (
              <>
                <button className="btn-secondary" onClick={() => setHistoryOpen(true)}>
                  <History className="h-4 w-4" /> View History
                </button>
                <button className="btn-secondary" onClick={() => navigate(`/formulas/${id}/calculator`)} title="Batch calculator (does not change the formula)">
                  <Calculator className="h-4 w-4" /> Calculator
                </button>
              </>
            )}
            <button className="btn-secondary" onClick={() => openPrint('formula')} title={header.isComplete ? 'Print' : 'Incomplete formulas cannot be printed'}>
              <Printer className="h-4 w-4" /> Print
            </button>
            <button
              className="btn-primary"
              onClick={() => submit()}
              disabled={save.isPending || (!dirty && !isNew)}
              title={header.isComplete ? 'Save' : 'Save as a draft (the formula stays Incomplete)'}
            >
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        }
      />

      <ErrorBanner message={serverError} />
      {scaleBusy && (
        <div className="mb-3 flex items-center gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-900">
          <Spinner /> {scaleBusy}
        </div>
      )}

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px] xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          {/* Devices: scale, dispense machine, label printer (legacy top bar) */}
          {hasDevices && ws && (
            <Card title="Devices" bodyClassName="p-4">
              <div className="grid gap-4 sm:grid-cols-3">
                {ws.scales.length > 0 && (
                  <DeviceSelect
                    icon={<Scale className="h-3.5 w-3.5" />}
                    label="Scale"
                    value={ws.scaleDeviceId}
                    options={ws.scales}
                    placeholder="Please select a scale..."
                    busy={setDevice.isPending && setDevice.variables?.kind === 'scale'}
                    onChange={(v) => {
                      setDevice.mutate({ kind: 'scale', deviceId: v })
                      // The scale unit decides the batch type: a kilogram scale weighs in Kilograms, a gram scale in Grams.
                      const s = ws.scales.find((x) => x.id === v)
                      const bt = s ? (s.unit === 'kg' ? 4 : 2) : null
                      if (bt && bt !== header.batchType) {
                        set('batchType', bt)
                        setAutoSave({})
                      }
                    }}
                  />
                )}
                {ws.dispensers.length > 0 && (
                  <DeviceSelect
                    icon={<Droplets className="h-3.5 w-3.5" />}
                    label="Dispense Machine"
                    value={ws.dispenserId}
                    options={ws.dispensers}
                    placeholder="Please select a Dispense Machine"
                    busy={setDevice.isPending && setDevice.variables?.kind === 'dispenser'}
                    onChange={(v) => setDevice.mutate({ kind: 'dispenser', deviceId: v })}
                  />
                )}
                {ws.printers.length > 0 && (
                  <DeviceSelect
                    icon={<Tag className="h-3.5 w-3.5" />}
                    label="Label Printer"
                    value={ws.printerDeviceId}
                    options={ws.printers}
                    placeholder="Please select a Label Printer..."
                    busy={setDevice.isPending && setDevice.variables?.kind === 'printer'}
                    onChange={(v) => setDevice.mutate({ kind: 'printer', deviceId: v })}
                  />
                )}
              </div>
              {purgeFailures.length > 0 && (
                <div className="mt-3">
                  <Note>
                    Please manually purge before dispensing. Failed purges on {dispenser?.bridgeName}:{' '}
                    {purgeFailures.map((p) => `canister ${p.canisterNumber} (${p.message ?? 'failed'}, ${dateTime(p.createdAt)})`).join('; ')}
                  </Note>
                </div>
              )}
            </Card>
          )}

          {/* Colour formula */}
          <Card
            title={
              <span className="inline-flex flex-wrap items-center gap-2">
                Color Formula <span className="badge bg-muted text-muted-foreground">{lines.length}</span>
                <span className="text-xs font-normal text-muted-foreground">Pigments {pct(sums.pigment)} · Dyes {pct(sums.dye)} of base</span>
              </span>
            }
            actions={
              !isNew ? (
                <button className="btn-ghost btn-sm" title="Export to Excel" onClick={() => download(`/formulas/${id}/export`, 'Formula.xlsx').catch(onError)}>
                  <FileSpreadsheet className="h-4 w-4 text-emerald-700" /> Excel
                </button>
              ) : undefined
            }
            bodyClassName="p-0"
          >
            {/* Material picker: Base Materials / Pigments / Dyes (+ Products) with Qty and batch, like the legacy three tables. */}
            <div className="space-y-2 border-b p-3">
              {locked && (
                <div className="flex items-center gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900" role="status">
                  <Lock className="h-4 w-4 shrink-0" /> {COMPLETE_LOCKED}
                </div>
              )}
              <div className="inline-flex flex-wrap rounded-md border bg-muted/40 p-0.5" role="group" aria-label="Material type">
                {[
                  { v: 0, l: 'All' },
                  { v: 1, l: 'Base Materials' },
                  { v: 2, l: 'Pigments' },
                  { v: 3, l: 'Dyes' },
                  { v: 7, l: 'Products' },
                ].map((o) => (
                  <button
                    key={o.v}
                    type="button"
                    aria-pressed={pickType === o.v}
                    onClick={() => setPickType(o.v)}
                    className={clsx('h-7 rounded px-2.5 text-xs font-medium', pickType === o.v ? 'bg-card text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground')}
                  >
                    {o.l}
                  </button>
                ))}
              </div>
              <div className="flex flex-col gap-2 md:flex-row md:items-start">
                <SearchSelect
                  className="flex-1"
                  options={materialOptions}
                  value={pickMaterial}
                  onChange={(v) => {
                    setPickMaterial(v)
                    setPickBatch('')
                  }}
                  placeholder={materialsQ.isLoading ? 'Loading materials…' : locked ? COMPLETE_LOCKED : 'Search materials to add to the formula'}
                  disabled={materialsQ.isLoading || gid <= 0 || locked}
                  emptyText={materialsQ.data ? 'No matching materials (already added materials are hidden)' : 'No results found'}
                />
                {usesBatches && pickedMaterial && (
                  <select className="input md:w-52" value={pickBatch} onChange={(e) => setPickBatch(e.target.value)} aria-label="Batch #">
                    {pickBatches.length === 0 ? (
                      <option value="">No Inventory Setup</option>
                    ) : (
                      <option value="">Latest batch (#{pickBatches[pickBatches.length - 1].batchNumber})</option>
                    )}
                    {pickBatches.map((b) => (
                      <option key={b.batchNumber} value={b.batchNumber}>
                        #{b.batchNumber} - {num(b.onHand, 2)} Gal
                      </option>
                    ))}
                  </select>
                )}
                <NumInput className="md:w-36" value={pickQty} onChange={setPickQty} suffix="g" placeholder={scale ? (scale.unit === 'kg' ? 'KiloGrams' : 'Grams') : 'Grams'} ariaLabel="Qty" onEnter={() => void addFromPicker()} />
                <button
                  className="btn-primary"
                  onClick={() => void addFromPicker()}
                  disabled={!pickMaterial || !!scaleBusy || locked}
                  title={lockTitle ?? (scaleId ? 'Leave Qty empty to take the weight from the scale' : undefined)}
                >
                  <Plus className="h-4 w-4" /> Add
                </button>
              </div>
              {materialsQ.isError && <div className="text-xs text-destructive">{errorMessage(materialsQ.error)}</div>}
              {missingDensity.length > 0 && (
                <Note>
                  The following material(s) do not have a setting for Density (lb/gal): {missingDensity.map((l) => `${l.productName}${l.productCode ? ` (${l.productCode})` : ''}`).join(', ')}.
                  Edit the material in Equipment &amp; Materials to set the density.
                </Note>
              )}
            </div>
            {lines.length === 0 ? (
              <EmptyState title="No ingredients yet" description="Pick a Base, Pigment, Dye or Product material, enter the grams (or read the scale) and click Add." icon={<FlaskConical className="h-5 w-5" />} />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="border-b bg-muted/60">
                    <tr>
                      <th className="th w-16">Order</th>
                      <th className="th w-8 text-center">T</th>
                      {showCalc && <th className="th text-center" title="Calc Batch">Calc</th>}
                      <th className="th">Prod Name</th>
                      <th className="th hidden md:table-cell">Prod #</th>
                      {usesBatches && <th className="th">Batch #</th>}
                      <th className="th text-right">Grams</th>
                      <th className="th text-right">%</th>
                      <th className="th text-right">Fl Oz</th>
                      <th className="th hidden text-right xl:table-cell">$/gal</th>
                      <th className="th hidden text-right lg:table-cell">Cost</th>
                      {showAddColumn && <th className="th">Add Material</th>}
                      <th className="th w-10" title="Remove Material" />
                      {usesBatches && !isNew && (
                        <>
                          <th className="th text-right">Amount to Dispense {scale ? `(${scale.unit})` : '(g)'}</th>
                          <th className="th text-right">Total Dispensed (g)</th>
                          <th className="th text-center">Dispense</th>
                        </>
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((l, i) => {
                      const g = gramsOf(l)
                      const gal = gallonsOf(g, l.density)
                      const oz = flOzOf(g, l.density)
                      const err = showErr(`line-${l.key}`)
                      const batches = batchesFor(l.materialId)
                      const short = notEnough(l)
                      const lineDirty = !l.id || gramsOf(l) !== l.savedGrams
                      return (
                        <tr key={l.key} className="border-b last:border-0 hover:bg-muted/30">
                          <td className="td">
                            <div className="flex items-center gap-0.5">
                              <span className="w-5 text-xs tabular-nums text-muted-foreground">{i + 1}</span>
                              <button className="btn-icon h-6 w-6" title={lockTitle ?? 'Move up'} disabled={i === 0 || locked} onClick={() => moveLine(i, -1)}>
                                <ArrowUp className="h-3.5 w-3.5" />
                              </button>
                              <button className="btn-icon h-6 w-6" title={lockTitle ?? 'Move down'} disabled={i === lines.length - 1 || locked} onClick={() => moveLine(i, 1)}>
                                <ArrowDown className="h-3.5 w-3.5" />
                              </button>
                            </div>
                          </td>
                          <td className="td text-center">
                            <span className="inline-flex items-center gap-1 font-semibold" title={l.materialTypeLabel}>
                              {l.colorCode && <span className="inline-block h-2.5 w-2.5 rounded-sm border" style={{ backgroundColor: l.colorCode }} />}
                              {typeLetter(l.materialType)}
                            </span>
                          </td>
                          {showCalc && (
                            <td className="td text-center">
                              {l.materialType === 1 && l.id && (
                                <button
                                  className="btn-icon h-7 w-7 text-primary"
                                  title={lockTitle ?? 'Calc Batch — recalculate the formula from the weighed amount of this base'}
                                  disabled={busyWorkspace || !!scaleBusy || locked}
                                  onClick={() => void onRecalc(l)}
                                >
                                  <RefreshCw className="h-4 w-4" />
                                </button>
                              )}
                            </td>
                          )}
                          <td className="td min-w-[10rem]">
                            <div className="font-medium">{l.productName}</div>
                            <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                              {l.categoryName && <span>{l.categoryName}</span>}
                              <span className="md:hidden">{l.productCode}</span>
                              {l.materialDeleted && <span className="badge bg-destructive/10 text-destructive">deleted material</span>}
                            </div>
                          </td>
                          <td className="td hidden md:table-cell">{l.productCode}</td>
                          {usesBatches && (
                            <td className="td min-w-[9rem]">
                              {batches.length === 0 ? (
                                <span className="text-xs text-muted-foreground">No Inventory Setup</span>
                              ) : batches.length === 1 && !isNew && !l.batchNumber ? (
                                <span className="text-xs">#{batches[0].batchNumber} - {num(batches[0].onHand, 2)}</span>
                              ) : (
                                <select
                                  className="input h-8 text-xs"
                                  value={l.batchNumber ?? ''}
                                  disabled={setBatch.isPending}
                                  aria-label={`Batch # of ${l.productName}`}
                                  onChange={(e) => {
                                    const v = e.target.value || null
                                    if (l.id) setBatch.mutate({ line: l, batchNumber: v })
                                    else setLines((ls) => ls.map((x) => (x.key === l.key ? { ...x, batchNumber: v } : x)))
                                  }}
                                >
                                  <option value="">Select batch...</option>
                                  {l.batchNumber && !batches.some((b) => b.batchNumber === l.batchNumber) && <option value={l.batchNumber}>#{l.batchNumber} - 0 Gal</option>}
                                  {batches.map((b) => (
                                    <option key={b.batchNumber} value={b.batchNumber}>
                                      #{b.batchNumber} - {num(b.onHand, 2)} Gal
                                    </option>
                                  ))}
                                </select>
                              )}
                              {short && (
                                <Link
                                  to={`/materials#${MATERIAL_TAB[l.materialType] ?? 'base'}`}
                                  className="mt-0.5 block text-[11px] font-medium text-red-800 underline-offset-2 hover:underline"
                                  title="Add material to the batch in Equipment & Materials"
                                >
                                  Not Enough Material in Batch
                                </Link>
                              )}
                            </td>
                          )}
                          <td className="td text-right">
                            <NumInput
                              className="ml-auto w-28"
                              value={l.grams}
                              onChange={(v) => updateGrams(l.key, v)}
                              suffix="g"
                              invalid={!!err}
                              autoFocus={l.key === lastAdded}
                              ariaLabel={`Grams of ${l.productName}`}
                              readOnly={locked}
                            />
                            {err && <div className="mt-0.5 text-[11px] text-destructive">{err}</div>}
                          </td>
                          <td className="td text-right tabular-nums">{totals.totalGrams > 0 ? `${num((g / totals.totalGrams) * 100, 2)}%` : '—'}</td>
                          <td className={clsx('td text-right tabular-nums', oz === null && 'text-destructive')} title={oz === null ? 'No density (lb/gal) for this material' : undefined}>
                            {oz === null ? 'Error' : num(oz, 4)}
                          </td>
                          <td className="td hidden text-right tabular-nums xl:table-cell">{money(l.price)}</td>
                          <td className="td hidden text-right tabular-nums lg:table-cell">{money(gal * l.price)}</td>
                          {showAddColumn && (
                            <td className="td">
                              <div className="flex items-center gap-1">
                                <button className="btn-icon h-7 w-7" title={lockTitle ?? 'Add material (from the scale when no grams are entered)'} disabled={!!scaleBusy || locked} onClick={() => void addToRow(l)}>
                                  <PlusCircle className="h-4 w-4" />
                                </button>
                                {l.materialType !== 1 && (
                                  <input
                                    className="input h-7 w-20 px-1.5 text-xs tabular-nums"
                                    placeholder={scale?.unit === 'kg' ? 'KiloGrams' : 'Grams'}
                                    inputMode="decimal"
                                    value={rowAdd[l.key] ?? ''}
                                    onChange={(e) => setRowAdd((r) => ({ ...r, [l.key]: e.target.value }))}
                                    onKeyDown={(e) => e.key === 'Enter' && void addToRow(l)}
                                    aria-label={`Grams to add to ${l.productName}`}
                                  />
                                )}
                              </div>
                            </td>
                          )}
                          <td className="td">
                            <button className="btn-icon hover:text-destructive" title={lockTitle ?? 'Remove Material'} disabled={locked} onClick={() => removeLine(l.key)}>
                              <Trash2 className="h-4 w-4" />
                            </button>
                          </td>
                          {usesBatches && !isNew && (
                            <>
                              <td className={clsx('td text-right tabular-nums', lineDirty && 'text-muted-foreground')} title={lineDirty ? 'After saving' : undefined}>
                                {num(projectedDispense(l), 4)}
                              </td>
                              <td className="td text-right tabular-nums">{num(l.dispensedGrams, 4)}</td>
                              <td className="td">
                                {l.id && (
                                  <div className="flex items-center justify-center gap-0.5">
                                    <button
                                      className={clsx('btn-icon h-7 w-7', l.dispenseAmount > 0 && !l.isDispensed ? 'text-primary' : 'text-muted-foreground')}
                                      title={dirty ? SAVE_FIRST : 'Dispensed — add the Amount to Dispense to Total Dispensed'}
                                      disabled={dirty || busyWorkspace || l.dispenseAmount <= 0}
                                      onClick={() => rowAction.mutate({ line: l, action: 'mark-dispensed' })}
                                    >
                                      <PlusCircle className="h-4 w-4" />
                                    </button>
                                    {(l.isDispensed || l.dispensedGrams > 0) && (
                                      <button
                                        className="btn-icon h-7 w-7 text-primary"
                                        title={dirty ? SAVE_FIRST : 'Undo — move Total Dispensed back to Amount to Dispense'}
                                        disabled={dirty || busyWorkspace}
                                        onClick={() => rowAction.mutate({ line: l, action: 'revert-dispensed' })}
                                      >
                                        <MinusCircle className="h-4 w-4" />
                                      </button>
                                    )}
                                    <button
                                      className="btn-icon h-7 w-7"
                                      title={dirty ? SAVE_FIRST : 'Clear Amount to Dispense and Total Dispensed'}
                                      disabled={dirty || busyWorkspace}
                                      onClick={() => rowAction.mutate({ line: l, action: 'reset-dispense' })}
                                    >
                                      <XSquare className="h-4 w-4" />
                                    </button>
                                  </div>
                                )}
                              </td>
                            </>
                          )}
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="border-t-2 bg-muted/40 font-semibold">
                    <tr>
                      <td className="td" colSpan={showCalc ? 4 : 3}>
                        Total
                      </td>
                      <td className="td hidden md:table-cell" />
                      {usesBatches && <td className="td" />}
                      <td className="td text-right tabular-nums">{num(totals.totalGrams, 2)} g</td>
                      <td className="td text-right tabular-nums">{totals.totalGrams > 0 ? '100%' : '—'}</td>
                      <td className="td text-right tabular-nums">{num(totals.totalGallons * 128, 2)}</td>
                      <td className="td hidden xl:table-cell" />
                      <td className="td hidden text-right tabular-nums lg:table-cell">{money(totals.materialCost)}</td>
                      {showAddColumn && <td className="td" />}
                      <td className="td" />
                      {usesBatches && !isNew && (
                        <>
                          <td className="td text-right tabular-nums">{num(lines.reduce((s, l) => s + projectedDispense(l), 0), 2)}</td>
                          <td className="td text-right tabular-nums">{num(lines.reduce((s, l) => s + l.dispensedGrams, 0), 2)}</td>
                          <td className="td" />
                        </>
                      )}
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
          </Card>

          {/* Batch size, reweigh, dispense / record */}
          <Card title="Batch Size">
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Field label={`Batch Size (${batchUnit})`} hint={batchBlockReason ?? lockTitle ?? 'Changing it rescales every ingredient (saved at once).'}>
                <NumInput
                  value={batchDraft ?? (batchValue ? String(Math.round(batchValue * 10000) / 10000) : '')}
                  onChange={setBatchDraft}
                  onBlur={applyBatchSize}
                  onEnter={applyBatchSize}
                  suffix={batchUnit}
                  placeholder="0"
                  ariaLabel="Batch Size"
                />
              </Field>
              <Field label="Batch Type">
                <select className="input" value={header.batchType} onChange={(e) => changeBatchType(Number(e.target.value))} aria-label="Batch Type" title={batchBlockReason ?? undefined}>
                  {BATCH_TYPES.map((b) => (
                    <option key={b.value} value={b.value}>
                      {b.label}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label={`Reweigh Formula (g)`} hint={isNew ? 'Available after saving.' : scaleId ? 'Enter the weight or read it from the scale, then GO.' : 'Enter the new total weight, then GO.'}>
                <div className="flex gap-1.5">
                  <NumInput className="flex-1" value={reweigh} onChange={setReweigh} suffix="g" ariaLabel="Reweigh Formula" onEnter={onReweigh} />
                  {scaleId && (
                    <button className="btn-secondary btn-icon h-9 w-9" title="Read the weight from the scale" disabled={!!scaleBusy || isNew} onClick={() => void readReweighFromScale()}>
                      <Scale className="h-4 w-4" />
                    </button>
                  )}
                  <button className="btn-primary" onClick={onReweigh} disabled={isNew || reweighM.isPending || locked} title={lockTitle}>
                    {reweighM.isPending ? <Spinner /> : null} GO
                  </button>
                </div>
              </Field>
              <Field label="Total Formula Weight (g)">
                <div className="flex h-9 items-center text-lg font-semibold tabular-nums">{num(totals.totalGrams, 2)}</div>
              </Field>
            </div>
            {!isNew && (
              <div className="mt-4 flex flex-wrap items-center justify-end gap-2 border-t pt-4">
                {canMachineDispense && (
                  <button className="btn-primary" onClick={onDispense} disabled={dispense.isPending || !!dispenseCmd}>
                    {dispense.isPending ? <Spinner /> : <Droplets className="h-4 w-4" />} Dispense
                  </button>
                )}
                {(ws?.hasUndoDispense || detail.data?.hasUndoDispense) && (
                  <button className="btn-secondary" onClick={() => undo.mutate()} disabled={undo.isPending || dirty} title={dirty ? SAVE_FIRST : undefined}>
                    {undo.isPending ? <Spinner /> : <Undo2 className="h-4 w-4" />} Undo Dispense
                  </button>
                )}
                <button className="btn-secondary" onClick={onRecord} disabled={record.isPending}>
                  {record.isPending ? <Spinner /> : <RotateCcw className="h-4 w-4" />} Record &amp; Reset
                </button>
                <div className="inline-flex">
                  <button
                    className="btn-secondary rounded-r-none"
                    onClick={() => void printFormulaLabel()}
                    disabled={sendLabel.isPending || save.isPending}
                    title={hasPrinters ? 'Send the formula can label to the selected label printer' : 'Print the formula can label from the browser'}
                  >
                    {sendLabel.isPending && !sendLabel.variables?.customerLabel ? <Spinner /> : <Tag className="h-4 w-4" />} Print Formula Can Label
                  </button>
                  <button className="btn-secondary -ml-px rounded-l-none px-2.5" onClick={() => openPrint('label')} title="Preview the formula can label (browser print)" aria-label="Preview Formula Can Label">
                    <Eye className="h-4 w-4" />
                  </button>
                </div>
              </div>
            )}
            {isNew && <p className="mt-3 text-xs text-muted-foreground">Save the formula to use scales, batches, dispensing and labels.</p>}
          </Card>

          {/* Formula Price */}
          <Card title="Formula Price">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Formula Cost" hint="Material cost of the batch">
                <NumInput value={totals.materialCost.toFixed(2)} onChange={() => undefined} prefix="$" readOnly ariaLabel="Formula Cost" />
              </Field>
              <Field label="Add Mark-Up %" error={showErr('markUp')}>
                <NumInput value={header.markUp} onChange={(v) => set('markUp', v)} suffix="%" invalid={!!showErr('markUp')} ariaLabel="Add Mark-Up %" />
              </Field>
              <Field label="Add Container Price" error={showErr('containerPrice')}>
                <NumInput value={header.containerPrice} onChange={(v) => set('containerPrice', v)} prefix="$" invalid={!!showErr('containerPrice')} ariaLabel="Add Container Price" />
              </Field>
              <Field label="Container Type">
                <select className="input" value={header.containerType} onChange={(e) => set('containerType', e.target.value)} aria-label="Container Type">
                  <option value="">—</option>
                  {containerOptions.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
          </Card>

          {/* Formula Delta */}
          <Card title="Formula Delta">
            <div className="space-y-4">
              {(['spex', 'spin'] as const).map((p) => {
                const deKey = `${p}DeltaE` as DeltaKey
                const match = deltaEMatch(parseNum(header[deKey]))
                return (
                  <div key={p}>
                    <div className="mb-1.5 flex items-center gap-2">
                      <span className="text-sm font-semibold">{p.toUpperCase()}</span>
                      <span className="text-xs text-muted-foreground">{p === 'spin' ? 'Specular included' : 'Specular excluded'}</span>
                      {match && <span className={clsx('badge ml-auto', match.className)} title="Delta E">ΔE {header[deKey]} · {match.label}</span>}
                    </div>
                    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                      {(['L', 'A', 'B', 'E'] as const).map((c) => {
                        const k = `${p}Delta${c}` as DeltaKey
                        return (
                          <Field key={k} label={`Delta ${c}*`} error={showErr(k)}>
                            <NumInput value={header[k]} onChange={(v) => set(k, v)} invalid={!!showErr(k)} placeholder="—" ariaLabel={`${p} Delta ${c}`} />
                          </Field>
                        )
                      })}
                    </div>
                  </div>
                )
              })}
              <Field label="Substrate">
                <input className="input" value={header.substrate} maxLength={200} placeholder="e.g. Red oak" onChange={(e) => set('substrate', e.target.value)} />
              </Field>
            </div>
          </Card>

          {/* Formula Information */}
          <Card title="Formula Information">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Material Category" hint={categoriesQ.data && categoryOptions.length === 0 ? 'No Base, Formula or Product categories yet (Material Categories).' : undefined}>
                <SearchSelect options={categoryOptions} value={header.categoryId} onChange={(v) => set('categoryId', v)} placeholder={categoriesQ.isLoading ? 'Loading…' : 'Select a Category'} />
              </Field>
              <Field label="Employee Name" required={header.isComplete && requireEmployee} error={showErr('employeeName')}>
                <input className={clsx('input', showErr('employeeName') && 'input-invalid')} value={header.employeeName} maxLength={200} onChange={(e) => set('employeeName', e.target.value)} />
              </Field>
              <Field label="Formula Name" required error={showErr('name')}>
                <input className={clsx('input', showErr('name') && 'input-invalid')} value={header.name} maxLength={200} autoFocus={isNew} onChange={(e) => set('name', e.target.value)} />
              </Field>
              <Field label="Formula #" required={requireNumber && isNew} error={showErr('number')} hint={isNew ? undefined : 'Use Copy to New for a different number.'}>
                {/* Legacy FormulaPrice: the number of an existing formula is read-only. */}
                <input
                  className={clsx('input', showErr('number') && 'input-invalid', !isNew && 'bg-muted/50')}
                  value={header.number}
                  maxLength={100}
                  readOnly={!isNew}
                  aria-readonly={!isNew}
                  onChange={(e) => set('number', e.target.value)}
                  aria-label="Formula #"
                />
              </Field>
              <Field label="Total Weight (g)">
                <input className="input bg-muted/50 tabular-nums" readOnly value={num(totals.totalGrams, 2)} aria-label="Total Weight (g)" />
              </Field>
              <Field label="Batch Type">
                <input className="input bg-muted/50" readOnly value={BATCH_TYPES.find((b) => b.value === header.batchType)?.label ?? ''} aria-label="Batch Type (read-only)" />
              </Field>
              <Field label="Purchase Order #">
                <input className="input" value={header.purchaseOrderNumber} maxLength={200} onChange={(e) => set('purchaseOrderNumber', e.target.value)} />
              </Field>
              <Field label="Customer Name">
                <input className="input" value={header.customerName} maxLength={200} onChange={(e) => set('customerName', e.target.value)} />
              </Field>
              <Field label="Notes" className="sm:col-span-2">
                <textarea className="input" rows={3} maxLength={4000} value={header.notes} onChange={(e) => set('notes', e.target.value)} placeholder="Mixing instructions, application notes…" />
              </Field>
              {admin && !isNew && detail.data && (
                <Field label="Group" hint="Administrators can move the formula (ingredients, category and documents) to another group.">
                  <select
                    className="input"
                    value={detail.data.groupId}
                    disabled={moveGroup.isPending}
                    aria-label="Group"
                    onChange={(e) => {
                      const target = Number(e.target.value)
                      if (target === detail.data!.groupId) return
                      if (dirty) return toast.error(SAVE_FIRST)
                      setMoveTo(target)
                    }}
                  >
                    {groupOptions.map((g) => (
                      <option key={g.id} value={g.id}>
                        {g.name}
                      </option>
                    ))}
                  </select>
                </Field>
              )}
              <Field
                label="Status"
                error={showErr('isComplete')}
                hint={savedComplete ? undefined : 'Save below completes the formula (legacy step 2). The Save at the top keeps it Incomplete.'}
                className="sm:col-span-2"
              >
                <div className="flex flex-wrap items-center gap-3" data-testid="formula-status">
                  <StatusBadge complete={header.isComplete} />
                  {admin && savedComplete && (
                    <button className="btn-ghost btn-sm" onClick={() => submit({ complete: false })} disabled={save.isPending} title="Administrators: set the formula back to Incomplete">
                      Mark as Incomplete
                    </button>
                  )}
                </div>
              </Field>
            </div>
            <div className="mt-4 flex flex-wrap justify-end gap-2 border-t pt-4">
              <div className="inline-flex">
                <button
                  className="btn-secondary rounded-r-none"
                  onClick={() => void printCustomerLabel()}
                  disabled={isNew || sendLabel.isPending || save.isPending}
                  title={!header.isComplete ? 'Incomplete formulas cannot be printed' : hasPrinters ? 'Send the customer can label to your default label printer' : 'Print the customer can label from the browser'}
                >
                  {sendLabel.isPending && sendLabel.variables?.customerLabel ? <Spinner /> : <Tag className="h-4 w-4" />} Print Customer Can Label
                </button>
                <button
                  className="btn-secondary -ml-px rounded-l-none px-2.5"
                  onClick={() => openPrint('customerLabel')}
                  disabled={isNew}
                  title="Preview the customer can label (browser print)"
                  aria-label="Preview Customer Can Label"
                >
                  <Eye className="h-4 w-4" />
                </button>
              </div>
              <button
                className="btn-primary"
                onClick={() => submit({ complete: true })}
                disabled={save.isPending || (savedComplete && !dirty)}
                title={savedComplete ? 'Save' : 'Save the formula information and mark the formula Complete'}
                data-testid="save-complete"
              >
                {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
              </button>
              <button className="btn-secondary" onClick={() => openPrint('formula')}>
                <Printer className="h-4 w-4" /> Print
              </button>
            </div>
          </Card>
        </div>

        {/* Summary */}
        <aside className="space-y-5 lg:sticky lg:top-4 lg:self-start">
          <Card title="Summary" bodyClassName="p-4 pt-2">
            <div className="divide-y">
              <SummaryRow label="Total weight" value={`${num(totals.totalGrams, 2)} g`} hint={`${num(totals.totalPounds, 3)} lb`} />
              <SummaryRow label="Total gallons" value={num(totals.totalGallons, 4)} />
              <SummaryRow label="Formula cost" value={money(totals.materialCost)} />
              <SummaryRow label={`Mark-up (${num(markUp, 2)}%)`} value={money(totals.markUpAmount)} />
              <SummaryRow label={`Container${header.containerType ? ` (${header.containerType})` : ''}`} value={money(containerPrice)} />
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
          {!isNew && ws?.nozzle.cleanNozzleHours ? (
            <Card title="Dispensing" bodyClassName="p-4 text-sm">
              <div className="flex items-start gap-2">
                {ws.nozzle.cleaningRequired ? <AlertTriangle className="mt-0.5 h-4 w-4 text-amber-600" /> : <Droplets className="mt-0.5 h-4 w-4 text-primary" />}
                <div>
                  <div>Clean the nozzle {ws.nozzle.cleanNozzleHours} hour{ws.nozzle.cleanNozzleHours === 1 ? '' : 's'} after the last dispense.</div>
                  {ws.nozzle.lastDispensedAt && <div className="text-xs text-muted-foreground">Last dispense: {dateTime(ws.nozzle.lastDispensedAt)}</div>}
                  {ws.nozzle.cleaningRequired && (
                    <button className="btn-secondary btn-sm mt-2" onClick={() => setNozzleOpen(true)}>
                      Confirm nozzle cleaning
                    </button>
                  )}
                </div>
              </div>
            </Card>
          ) : null}
          <Card title="Composition by weight">
            <CompositionDonut items={lines.map((l) => ({ key: l.key, name: l.productName, grams: gramsOf(l) }))} />
          </Card>
        </aside>
      </div>

      {!isNew && detail.data && (
        <>
          <EntityDocuments open={docsOpen} onClose={() => setDocsOpen(false)} entityType="Formula" entityId={detail.data.id} groupId={detail.data.groupId} title={header.name || detail.data.name} />
          <FormulaHistoryModal open={historyOpen} onClose={() => setHistoryOpen(false)} formulaId={detail.data.id} title={`History — ${header.name || detail.data.name}`} />
          <NozzleConfirmModal open={nozzleOpen} onClose={() => setNozzleOpen(false)} groupId={detail.data.groupId} onConfirmed={() => qc.invalidateQueries({ queryKey: ['formula-workspace', id] })} />
        </>
      )}
      <RecalcAmountModal
        open={!!recalcLine}
        onClose={() => setRecalcLine(null)}
        busy={recalc.isPending}
        materialName={recalcLine?.productName}
        onSubmit={(grams) => recalcLine && recalc.mutate({ line: recalcLine, grams })}
      />
      <SelectLocationModal open={locationOpen} locations={ws?.locations ?? []} onClose={() => setLocationOpen(false)} busy={record.isPending} onSave={(locId) => record.mutate(locId)} />
      <DispensingModal
        commandId={dispenseCmd?.id ?? null}
        materials={dispenseCmd?.materials ?? []}
        onClose={() => {
          setDispenseCmd(null)
          refreshAll()
        }}
        onFinished={(s) => {
          if (s.status === 'Succeeded') toast.success('Dispensing completed.')
          else if (s.status !== 'Cancelled') toast.error(s.message ?? 'Dispensing failed')
          refreshAll()
        }}
      />
      <PrintDeniedModal open={printDenied} onClose={() => setPrintDenied(false)} />
      <ConfirmDialog
        open={moveTo !== null}
        title="Move Formula"
        danger={false}
        confirmLabel="Move"
        busy={moveGroup.isPending}
        onClose={() => setMoveTo(null)}
        onConfirm={() => moveTo !== null && moveGroup.mutate(moveTo)}
        message={`Move the formula "${header.name}" to the ${groupOptions.find((g) => g.id === moveTo)?.name ?? ''} group? Its category and ingredients are matched by name in that group (and created when missing); linked documents are copied along.`}
      />
    </>
  )
}
