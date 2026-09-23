/** A colour as measured, with the swatch colour the server worked out for it. */
export interface LabColor {
  l: number
  a: number
  b: number
  hex: string
}

/** How a reading was obtained (backend ColorSource). */
export const SOURCES = [
  { value: 1, label: 'Spectrophotometer', hint: 'Read on the device — the one to trust.' },
  { value: 2, label: 'Photo', hint: 'Taken from a photograph with a calibration card.' },
  { value: 3, label: 'Typed in', hint: 'From a supplier sheet or an older record.' },
] as const

/** How the stain went on (backend ApplicationMethod). */
export const METHODS = [
  { value: 1, label: 'Spray' },
  { value: 2, label: 'Wipe' },
  { value: 3, label: 'Brush' },
  { value: 4, label: 'Dip' },
  { value: 5, label: 'Roll' },
  { value: 9, label: 'Other' },
] as const

export interface ColorSampleRow {
  id: number
  groupId: number
  name: string
  woodSpecies: string
  sandingGrit?: number | null
  wood?: LabColor | null
  formulaId?: number | null
  formulaName: string
  concentration?: number | null
  method?: number | null
  coats?: number | null
  wetFilmMils?: number | null
  flashMinutes?: number | null
  sealer?: string | null
  topcoat?: string | null
  sheen?: number | null
  final: LabColor
  source: number
  measuredAt?: string | null
  photoFile?: string | null
  notes?: string | null
  createdAt: string
  updatedAt?: string | null
}

export interface ColorMatchRow {
  sampleId: number
  name: string
  woodSpecies: string
  formulaId?: number | null
  formulaName: string
  recordedConcentration?: number | null
  suggestedConcentration?: number | null
  method?: number | null
  coats?: number | null
  topcoat?: string | null
  sheen?: number | null
  deltaE: number
  grade: string
  predicted: LabColor
  recorded: LabColor
  confidence: number
  basis: string
  source: number
}

export interface ColorMatchResult {
  target: LabColor
  sampleCount: number
  matches: ColorMatchRow[]
}

export interface PhotoReading {
  l: number
  a: number
  b: number
  hex: string
  calibrated: boolean
  correctionDeltaE: number
  note: string
}

/** A box drawn on a photo, in fractions of its width and height. */
export interface Region {
  x: number
  y: number
  width: number
  height: number
}

export const methodLabel = (v?: number | null) => METHODS.find((m) => m.value === v)?.label ?? ''
export const sourceLabel = (v?: number | null) => SOURCES.find((s) => s.value === v)?.label ?? ''

/** The colours the match grades are shown in — the same wording the formula screens use for ΔE. */
export function gradeClass(deltaE: number) {
  if (deltaE <= 1) return 'bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-300'
  if (deltaE <= 2) return 'bg-sky-100 text-sky-800 dark:bg-sky-500/15 dark:text-sky-300'
  if (deltaE <= 3.5) return 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-300'
  return 'bg-rose-100 text-rose-900 dark:bg-rose-500/15 dark:text-rose-200'
}
