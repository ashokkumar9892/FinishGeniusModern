/** Row of GET /api/formulas. */
export interface FormulaRow {
  id: number
  groupId: number
  groupName: string
  categoryId?: number | null
  categoryName?: string | null
  name: string
  number?: string | null
  customerName?: string | null
  isComplete: boolean
  ingredientCount: number
  totalGrams: number
  totalGallons: number
  cost: number
  price: number
  batchSize: number
  gramsInBatch: number
  employeeName?: string | null
  mixedOn?: string | null
  hasDocs: boolean
  createdAt: string
  updatedAt?: string | null
}

export interface FormulaIngredientDto {
  id: number
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
  grams: number
  sequence: number
  /** "Amount to Dispense" (g). */
  dispenseAmount: number
  /** "Total Dispensed" (g). */
  dispensedGrams: number
  isDispensed: boolean
  batchNumber?: string | null
  gallons: number
  flOz: number
  cost: number
  percent: number
}

/** GET /api/formulas/{id}. */
export interface FormulaDetail {
  id: number
  groupId: number
  groupName: string
  groupLogoFile?: string | null
  categoryId?: number | null
  categoryName?: string | null
  name: string
  number?: string | null
  customerName?: string | null
  isComplete: boolean
  batchSize: number
  batchType: number
  batchTypeLabel: string
  batchValue: number
  containerType?: string | null
  containerPrice: number
  markUp: number
  substrate?: string | null
  notes?: string | null
  employeeName?: string | null
  purchaseOrderNumber?: string | null
  mixedOn?: string | null
  dispenserId?: number | null
  spinDeltaL?: number | null
  spinDeltaA?: number | null
  spinDeltaB?: number | null
  spinDeltaE?: number | null
  spexDeltaL?: number | null
  spexDeltaA?: number | null
  spexDeltaB?: number | null
  spexDeltaE?: number | null
  createdAt: string
  updatedAt?: string | null
  createdByName?: string | null
  /** Mirror material (type Formula) that process steps pick. */
  materialId?: number | null
  usesBatches: boolean
  hasUndoDispense: boolean
  ingredients: FormulaIngredientDto[]
}

/** Subset of GET /api/materials used by the ingredient picker. */
export interface MaterialOption {
  id: number
  productName: string
  productCode?: string | null
  materialType: number
  materialTypeLabel: string
  categoryName?: string | null
  density: number
  price: number
  voc: number
  hap: number
  tap: number
  minQuantity: number
  onHand: number
  /** Name as stored (old site's sort order); falls back to productName. */
  sortName?: string
}

export interface CategoryOption {
  id: number
  name: string
  materialType?: number
}

/** Category types a formula can belong to — Base, Formula and Product, like the legacy app (MaterialType in 1, 6, 7). */
export const FORMULA_CATEGORY_TYPES = [1, 6, 7]

/** Material types that can go into a formula (Base, Pigment, Dye, Product). */
export const INGREDIENT_TYPES = [1, 2, 3, 7]

/** Legacy BatchTypeEnum, in the legacy drop-down order. */
export const BATCH_TYPES: { value: number; label: string; unit: string }[] = [
  { value: 1, label: 'Gallons', unit: 'gal' },
  { value: 2, label: 'Grams', unit: 'g' },
  { value: 3, label: 'Litres', unit: 'L' },
  { value: 4, label: 'Kilograms', unit: 'kg' },
  { value: 5, label: 'Quarts', unit: 'qt' },
]

/** Legacy container types (DropDownHelper.GetContainerTypes). */
export const CONTAINER_TYPES = ['1 Gallon', '5 Gallons', 'Drum', 'Quartz']

// ---------------------------------------------------------------- workspace (devices, batches, dispensing)

export interface WorkspaceDevice {
  id: number
  name: string
  bridgeName?: string | null
  bridgeOnline?: boolean | null
}

export interface ScaleDevice extends WorkspaceDevice {
  unit: 'g' | 'kg'
}

export interface DispenserDevice extends WorkspaceDevice {
  bridgeId?: number | null
  canisters: { canisterNo: number; materialId: number | null }[]
}

export interface InventoryBatch {
  batchNumber: string
  /** Gallons on hand. */
  onHand: number
}

/** GET /api/formulas/{id}/workspace. */
export interface Workspace {
  scales: ScaleDevice[]
  dispensers: DispenserDevice[]
  printers: WorkspaceDevice[]
  scaleDeviceId?: number | null
  printerDeviceId?: number | null
  dispenserId?: number | null
  usesBatches: boolean
  /** Batches with stock per material id. */
  batches: Record<string, InventoryBatch[]>
  locations: { id: number; name: string }[]
  nozzle: { cleanNozzleHours: number; isNozzleCleaned: boolean; lastDispensedAt?: string | null; cleaningRequired: boolean }
  purgeFailures: { bridgeDeviceId: number; canisterNumber: number; message?: string | null; createdAt: string }[]
  hasUndoDispense: boolean
  openDispenseCommandId?: number | null
}

/** GET /api/dispensing/commands/{id}. */
export interface CommandStatus {
  id: number
  commandType: string
  status: 'Pending' | 'Sent' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Expired'
  done: boolean
  message?: string | null
  weightGrams?: number | null
  createdAt: string
  sentAt?: string | null
  completedAt?: string | null
}

/** GET /api/formulas/{id}/history ("View History"). */
export interface HistoryRow {
  id: number
  userName: string
  field: string
  description?: string | null
  oldValue?: string | null
  valueAdded?: string | null
  newValue?: string | null
  employeeName?: string | null
  date: string
}

/** GET /api/dispensing/settings. */
export interface DispenseSettings {
  cleanNozzleHours: number
  isNozzleCleaned: boolean
  lastDispensedAt?: string | null
  cleaningRequired: boolean
  hasDispensers: boolean
  hasBridges: boolean
  /** A network bridge of the group is connected right now (Purge / machine dispense need one). */
  hasOnlineBridge: boolean
}

/** GET /api/formulas/{id}/usage. */
export interface FormulaUsage {
  processSteps: { id: number; name: string }[]
  processSchedules: { id: number; name: string; number: string }[]
}

/** "Complete formulas can only be changed by an administrator." (legacy FG Pro lock). */
export const COMPLETE_LOCKED = 'Complete formulas can only be changed by an administrator.'
