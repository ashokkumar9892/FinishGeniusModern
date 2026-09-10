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
  materialDeleted: boolean
  density: number
  price: number
  voc: number
  hap: number
  tap: number
  grams: number
  sequence: number
  gallons: number
  cost: number
  percent: number
}

/** GET /api/formulas/{id}. */
export interface FormulaDetail {
  id: number
  groupId: number
  groupName: string
  categoryId?: number | null
  categoryName?: string | null
  name: string
  number?: string | null
  customerName?: string | null
  isComplete: boolean
  batchSize: number
  containerType?: string | null
  containerPrice: number
  markUp: number
  substrate?: string | null
  notes?: string | null
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
}

export interface CategoryOption {
  id: number
  name: string
}

/** Material types that can go into a formula (Base, Pigment, Dye, Product). */
export const INGREDIENT_TYPES = [1, 2, 3, 7]
