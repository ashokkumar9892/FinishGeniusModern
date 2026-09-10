import { useQuery } from '@tanstack/react-query'
import { api, download } from '@/lib/api'
import { MaterialType } from '@/lib/types'

// ---------------------------------------------------------------------------
// DTOs returned by the Materials module endpoints
// ---------------------------------------------------------------------------

export interface Material {
  id: number
  groupId: number
  groupName: string
  materialType: number
  materialTypeLabel: string
  categoryId: number | null
  categoryName: string | null
  productCode: string | null
  productName: string
  density: number
  price: number
  voc: number
  hap: number
  tap: number
  minQuantity: number
  onHand: number
  vendorId: number | null
  vendorName?: string | null
  notes: string | null
  createdAt: string
  updatedAt: string | null
}

export interface Characteristic {
  id: number
  categoryId: number
  name: string
  unit: string | null
  inputType: string
  calcVariable: string | null
  defaultValue: string | null
  sequence: number
}

export interface Category {
  id: number
  groupId: number
  name: string
  materialType: number
  materialTypeLabel: string
  filter1: string | null
  filter2: string | null
  materialCount: number
  characteristics: Characteristic[]
}

export interface Vendor {
  id: number
  groupId: number
  groupName: string
  vendorName: string
  address: string | null
  city: string | null
  state: string | null
  zip: string | null
  country: string | null
  paymentTerms: string | null
  accountNumber: string | null
  contactName: string | null
  officePhone: string | null
  mobilePhone: string | null
  vendorEmail: string | null
  requestorEmail: string | null
}

export interface MaterialLocation {
  id: number
  groupId: number
  name: string
  materialType: number
  materialTypeLabel: string
}

export interface InventoryEntry {
  id: number
  createdAt: string
  quantity: number
  locationId: number | null
  locationName: string | null
  batchNumber: string | null
  reason: string | null
  customerName: string | null
  userName: string | null
}

export interface PurchaseOrderSummary {
  id: number
  groupId: number
  poNumber: string
  vendorId: number
  vendorName: string
  createdAt: string
  deliveryDate: string | null
  lineCount: number
  total: number
  createdByName: string | null
}

export interface PurchaseOrderDetail {
  id: number
  groupId: number
  groupName: string | null
  poNumber: string
  deliveryDate: string | null
  createdAt: string
  shipName: string | null
  shipAddress1: string | null
  shipAddress2: string | null
  shipCity: string | null
  shipState: string | null
  shipZip: string | null
  shipCountry: string | null
  createdByName: string | null
  createdByEmail: string | null
  vendor: Omit<Vendor, 'groupId' | 'groupName'>
  lines: {
    id: number
    materialId: number
    productName: string | null
    productCode: string | null
    categoryName: string | null
    materialTypeLabel: string | null
    quantity: number
    quantityType: string | null
    unitPrice: number
    lineTotal: number
  }[]
  total: number
}

export interface ImportResult {
  message: string
  created: number
  skipped: number
  categoriesCreated: number
  documentsLinked: number
  errors: string[]
  warnings: string[]
}

export interface MessageResponse {
  message: string
  id?: number
}

// ---------------------------------------------------------------------------
// Constants / helpers
// ---------------------------------------------------------------------------

/** Types that can be created on the Equipment & Materials screen (Formula categories belong to Formulas). */
export const editableTypes = [
  { value: MaterialType.Base, label: 'Base' },
  { value: MaterialType.Pigment, label: 'Pigment' },
  { value: MaterialType.Dye, label: 'Dye' },
  { value: MaterialType.Equipment, label: 'Equipment' },
  { value: MaterialType.Sundry, label: 'Sundry' },
  { value: MaterialType.Product, label: 'Product' },
]

export const isPieceType = (type: number) => type === MaterialType.Equipment || type === MaterialType.Sundry

/** Unit shown next to quantities: gallons for liquids, pieces for equipment / sundries. */
export const unitLabel = (type: number) => (isPieceType(type) ? 'pc' : 'gal')

export const quantityTypes = ['Gal', 'Qt', 'Pint', 'Lb', 'Pc', 'Each', 'Case', 'Pail (5 gal)', 'Drum (55 gal)', 'Tote (275 gal)']

export const defaultQuantityType = (type: number) => (isPieceType(type) ? 'Pc' : 'Gal')

export const isBelowMin = (m: Pick<Material, 'minQuantity' | 'onHand'>) => m.minQuantity > 0 && m.onHand < m.minQuantity

/** Quantity still needed to reach the minimum. */
export const jobToOrderQty = (m: Pick<Material, 'minQuantity' | 'onHand'>) => Math.max(0, m.minQuantity - m.onHand)

export const documentExtensions = ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'csv', 'txt', 'jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp']

export const downloadImportTemplate = () => download('/material-import/template', 'MaterialsImportTemplate.xlsx')

export async function uploadMaterials(groupId: number, file: File, documents: File[]) {
  const form = new FormData()
  form.append('groupId', String(groupId))
  form.append('file', file)
  documents.forEach((d) => form.append('documents', d))
  const res = await api.post<ImportResult>('/material-import', form, { headers: { 'Content-Type': 'multipart/form-data' } })
  return res.data
}

/** Parses a numeric text input; returns null for invalid input and 0 for blank. */
export function parseNum(v: string): number | null {
  const s = v.replace(/[$,\s]/g, '')
  if (s === '') return 0
  const n = Number(s)
  return Number.isFinite(n) ? n : null
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

export function useMaterials(groupId: number) {
  return useQuery({
    queryKey: ['materials', groupId, 'all'],
    queryFn: () => api.get<Material[]>('/materials', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
}

export function useVendors(groupId: number) {
  return useQuery({
    queryKey: ['vendors', groupId],
    queryFn: () => api.get<Vendor[]>('/vendors', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
}

export function useCategories(groupId: number, type?: number | null) {
  return useQuery({
    queryKey: ['material-categories', groupId, type ?? 'all'],
    queryFn: () => api.get<Category[]>('/material-categories', { params: { groupId, type: type ?? undefined } }).then((r) => r.data),
    enabled: groupId > 0,
  })
}

export function useLocations(groupId: number, type?: number | null, enabled = true) {
  return useQuery({
    queryKey: ['material-locations', groupId, type ?? 'all'],
    queryFn: () => api.get<MaterialLocation[]>('/material-locations', { params: { groupId, type: type ?? undefined } }).then((r) => r.data),
    enabled: enabled && groupId > 0,
  })
}
