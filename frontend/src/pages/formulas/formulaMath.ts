/**
 * Formula maths — mirrors backend Controllers/FormulasController.cs (FormulaCalc) so the editor updates live.
 *   gallons      = grams / (density lb/gal × 453.59237)   (0 when density is 0; density < 3 is legacy g/cc)
 *   fl oz        = gallons × 128
 *   cost         = gallons × price $/gal
 *   % of batch   = grams / total grams
 *   VOC/HAP/TAP  = Σ(gallons × value) / Σ gallons          (lb/gal of the mix)
 *   price        = cost × (1 + markUp / 100) + container price
 */
export const GRAMS_PER_POUND = 453.59237
export const FL_OZ_PER_GALLON = 128
export const LITRES_PER_GALLON = 3.78541178
export const QUARTS_PER_GALLON = 4

export interface CalcLine {
  grams: number
  density: number
  price: number
  voc: number
  hap: number
  tap: number
}

export interface FormulaTotals {
  totalGrams: number
  totalPounds: number
  totalGallons: number
  materialCost: number
  markUpAmount: number
  containerPrice: number
  price: number
  voc: number
  hap: number
  tap: number
  costPerGallon: number
  pricePerGallon: number
}

/** Density is lb/gal; values below 3 are legacy g/cc data (matches backend FormulaCalc.Gallons). */
export const gallonsOf = (grams: number, density: number) =>
  density > 0 ? grams / (density * (density < 3 ? 3785.41 : GRAMS_PER_POUND)) : 0

/** Fluid ounces (legacy "Fl Oz"); null when the material has no density (legacy shows "Error"). */
export const flOzOf = (grams: number, density: number) => (density > 0 ? gallonsOf(grams, density) * FL_OZ_PER_GALLON : null)

export function computeTotals(lines: CalcLine[], markUp: number, containerPrice: number): FormulaTotals {
  let grams = 0, gallons = 0, cost = 0, voc = 0, hap = 0, tap = 0
  for (const l of lines) {
    const g = Math.max(0, l.grams || 0)
    const gal = gallonsOf(g, l.density)
    grams += g
    gallons += gal
    cost += gal * l.price
    voc += gal * l.voc
    hap += gal * l.hap
    tap += gal * l.tap
  }
  const markUpAmount = cost * (markUp / 100)
  const price = cost + markUpAmount + containerPrice
  return {
    totalGrams: grams,
    totalPounds: grams / GRAMS_PER_POUND,
    totalGallons: gallons,
    materialCost: cost,
    markUpAmount,
    containerPrice,
    price,
    voc: gallons > 0 ? voc / gallons : 0,
    hap: gallons > 0 ? hap / gallons : 0,
    tap: gallons > 0 ? tap / gallons : 0,
    costPerGallon: gallons > 0 ? cost / gallons : 0,
    pricePerGallon: gallons > 0 ? price / gallons : 0,
  }
}

/** Legacy batch types: 1 Gallons, 2 Grams, 3 Litres, 4 Kilograms, 5 Quarts. Weight types scale by grams, volume types by gallons. */
export const isWeightBatchType = (t: number) => t === 2 || t === 4

/** The batch expressed in the unit of batch type `t`. */
export function batchValueOf(t: number, grams: number, gallons: number): number {
  switch (t) {
    case 4:
      return grams / 1000
    case 1:
      return gallons
    case 3:
      return gallons * LITRES_PER_GALLON
    case 5:
      return gallons * QUARTS_PER_GALLON
    default:
      return grams
  }
}

/** Factor that turns the current batch into `value` units of batch type `t` (null when it cannot be computed). */
export function batchScaleFactor(t: number, value: number, grams: number, gallons: number): number | null {
  const current = batchValueOf(t, grams, gallons)
  if (!(value > 0) || !(current > 0)) return null
  return value / current
}

/** Colour-match verdict for a ΔE value (CIE): ≤1 excellent, ≤2 commercial, otherwise review. */
export function deltaEMatch(de: number | null | undefined): { label: string; className: string } | null {
  if (de == null || !Number.isFinite(de)) return null
  if (de <= 1) return { label: 'Excellent match', className: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-300' }
  if (de <= 2) return { label: 'Commercial match', className: 'bg-sky-100 text-sky-800 dark:bg-sky-500/15 dark:text-sky-300' }
  return { label: 'Marginal — review', className: 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-300' }
}

/**
 * Rescales ingredient grams by `factor` (rounded to 0.0001 g, like the legacy page's 4 decimals).
 */
export function scaleByFactor(grams: number[], factor: number): number[] {
  return grams.map((g) => Math.round(Math.max(0, g) * factor * 10000) / 10000)
}

/**
 * Rescales ingredient grams proportionally so they add up to `target` grams (rounded to 0.01 g; the rounding
 * remainder goes to the largest ingredient so the total is exact).
 */
export function scaleGrams(grams: number[], target: number): number[] {
  const total = grams.reduce((s, g) => s + (g > 0 ? g : 0), 0)
  if (total <= 0 || target <= 0) return grams
  const factor = target / total
  const scaled = grams.map((g) => Math.round(Math.max(0, g) * factor * 100) / 100)
  const diff = Math.round((target - scaled.reduce((s, g) => s + g, 0)) * 100) / 100
  if (diff !== 0) {
    let big = 0
    scaled.forEach((g, i) => (g > scaled[big] ? (big = i) : undefined))
    scaled[big] = Math.round((scaled[big] + diff) * 100) / 100
  }
  return scaled
}

/** Parses a numeric input; empty / invalid → null. */
export function parseNum(v: string): number | null {
  const s = v.trim().replace(/[$,%\s]/g, '')
  if (s === '' || s === '-' || s === '.') return null
  const n = Number(s)
  return Number.isFinite(n) ? n : null
}

/** Type letter of the legacy "T" column. */
export const typeLetter = (materialType: number) => ({ 1: 'B', 2: 'P', 3: 'D', 7: 'Pr' })[materialType] ?? '?'

/** Legacy print order: Base, Pigment, Dye, then everything else. */
export const typeOrder = (materialType: number) => ({ 1: 0, 2: 1, 3: 2 })[materialType] ?? 3
