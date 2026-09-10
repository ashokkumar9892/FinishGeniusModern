// Types for the Process module (sub steps, process steps, schedules, quantities, pricing).

export interface PullDown {
  id: number
  subStepId: number
  sequence: number
  header: string
  choiceName?: string | null
  materialType?: number | null
  materialTypeLabel?: string | null
  categoryFilter1?: string | null
  categoryFilter2?: string | null
  query: string
}

export interface SubStep {
  id: number
  industrySectorId: number
  userRole: 'User' | 'Admin' | string
  name: string
  shortName: string
  sequence: number
  passThroughs: number
  webLink?: string | null
  instruction?: string | null
  pullDownCount: number
  usedBySteps?: number
  pullDowns: PullDown[]
}

export interface ProcessStepRow {
  id: number
  groupId: number
  groupName: string
  name: string
  industrySectorId: number
  industrySectorName: string
  filledCount: number
  scheduleCount: number
  createdAt: string
  updatedAt?: string | null
}

export interface CharacteristicDef {
  id: number
  name: string
  unit?: string | null
  inputType: 'Text' | 'Number' | 'Material' | 'YesNo' | 'Notes' | string
  calcVariable?: string | null
  defaultValue?: string | null
  sequence: number
}

export interface BuilderCategory {
  id: number
  name: string
  materialType: number
  materialTypeLabel: string
  filter1?: string | null
  filter2?: string | null
  characteristics: CharacteristicDef[]
}

export interface BuilderPullDown {
  id: number
  sequence: number
  header: string
  choiceName?: string | null
  materialType?: number | null
  categoryFilter1?: string | null
  categoryFilter2?: string | null
  query: string
  categories: BuilderCategory[]
}

export interface BuilderSubStep {
  id: number
  userRole: string
  name: string
  shortName: string
  sequence: number
  passThroughs: number
  webLink?: string | null
  instruction?: string | null
  pullDowns: BuilderPullDown[]
}

export interface BuilderValue {
  valueId: number
  characteristicId: number
  value?: string | null
  materialId?: number | null
  materialName?: string | null
}

export interface BuilderEntry {
  id: number
  subStepId: number
  pass: number
  pullDownId?: number | null
  categoryId?: number | null
  values: BuilderValue[]
}

export interface BuilderPayload {
  groupId: number
  groupName: string
  industrySectorId: number
  industrySectorName: string
  step: { id: number; name: string; groupId: number; industrySectorId: number; createdAt: string; updatedAt?: string | null } | null
  subSteps: BuilderSubStep[]
  entries: BuilderEntry[]
}

export interface StepValueView {
  valueId: number
  characteristicId: number
  characteristic: string
  unit?: string | null
  inputType: string
  calcVariable?: string | null
  value?: string | null
  materialId?: number | null
  materialName?: string | null
  display: string
}

export interface StepEntryView {
  entryId: number
  pullDownId?: number | null
  header?: string | null
  categoryId?: number | null
  categoryName?: string | null
  values: StepValueView[]
}

export interface StepPassView {
  subStepId: number
  sequence: number
  pass: number
  label: string
  name: string
  shortName: string
  userRole: string
  filled: boolean
  entries: StepEntryView[]
}

export interface StepView {
  id: number
  name: string
  groupId: number
  groupName: string
  industrySectorId: number
  industrySectorName?: string | null
  updatedAt?: string | null
  passes: StepPassView[]
}

export interface ScheduleRow {
  id: number
  groupId: number
  groupName: string
  name: string
  number: string
  customerName?: string | null
  departmentId?: number | null
  departmentName?: string | null
  isArchived: boolean
  stepCount: number
  createdAt: string
  updatedAt?: string | null
}

export interface ScheduleStepRow {
  scheduleStepId: number
  processStepId: number
  name: string
  originalName: string
  nameOverride?: string | null
  ordering: number
  hasOverrides: boolean
  overrideCount: number
  industrySectorName?: string | null
  processStepDeleted: boolean
}

export interface ScheduleDetail {
  id: number
  groupId: number
  groupName: string
  name: string
  number: string
  customerName?: string | null
  departmentId?: number | null
  isArchived: boolean
  createdAt: string
  updatedAt?: string | null
  steps: ScheduleStepRow[]
}

export interface StepEditValue {
  valueId: number
  subStepLabel: string
  subStepName: string
  header?: string | null
  categoryName?: string | null
  categoryId?: number | null
  characteristic: string
  unit?: string | null
  inputType: string
  calcVariable?: string | null
  originalValue?: string | null
  originalMaterialId?: number | null
  originalMaterialName?: string | null
  originalDisplay: string
  overrideValue?: string | null
  overrideMaterialId?: number | null
  overrideMaterialName?: string | null
  minValue?: number | null
  maxValue?: number | null
}

export interface StepEditPayload {
  scheduleStepId: number
  scheduleId: number
  scheduleName: string
  groupId: number
  processStepId: number
  originalName: string
  nameOverride?: string | null
  name: string
  values: StepEditValue[]
  passes: StepPassView[]
}

export interface PrintValue {
  processStepValueId: number
  characteristic: string
  unit?: string | null
  inputType: string
  calcVariable?: string | null
  value?: string | null
  materialName?: string | null
  display: string
  minValue?: number | null
  maxValue?: number | null
}

export interface SchedulePrint {
  id: number
  name: string
  number: string
  customerName?: string | null
  groupId: number
  groupName: string
  departmentName?: string | null
  isArchived: boolean
  createdAt: string
  updatedAt?: string | null
  printedAt: string
  steps: {
    number: number
    scheduleStepId: number
    processStepId: number
    name: string
    originalName: string
    industrySectorName?: string | null
    passes: { label: string; name: string; instruction?: string | null; values: PrintValue[] }[]
  }[]
}

export interface MaterialQuantityLine {
  materialId: number
  name: string
  productCode?: string | null
  quantity: number
  unit: string
  price: number
  cost: number
}

export interface QuantityResult {
  squareFootage: number
  productionHours: number
  materials: MaterialQuantityLine[]
  materialCostPerSqFt: number
  hoursPerSqFt: number
  otherCostPerSqFt: number
  totalCost: number
  hasCoverage: boolean
  hasMaterials: boolean
  hasProductionRate: boolean
}

export interface PricingInput {
  laborRate: number
  markUp: number
  premiumMarkUp: number
  oneSidedComplexity: number
  oneSidedArea: number
  twoSidedComplexity: number
  twoSidedArea: number
  highComplexity: number
  highComplexityArea: number
}

export interface PricingRow {
  label: string
  area: number
  complexityPercent: number
  sides: number
  cost: number
}

export interface PricingResult {
  totalPrice: number
  totalSquareFootage: number
  baseCostPerSqFt: number
  materialCostPerSqFt: number
  laborCostPerSqFt: number
  otherCostPerSqFt: number
  rows: PricingRow[]
  subtotal: number
  markUpAmount: number
  premiumMarkUpAmount: number
}

export interface Estimates {
  id: number
  name: string
  number: string
  customerName?: string | null
  groupId: number
  groupName: string
  isArchived: boolean
  oneSidedArea: number
  twoSidedArea: number
  laborRate: number
  markUp: number
  premiumMarkUp: number
  oneSidedComplexity: number
  oneSidedPriceArea: number
  twoSidedComplexity: number
  twoSidedPriceArea: number
  highComplexity: number
  highComplexityArea: number
  totalJobPrice: number
  updatedAt?: string | null
}

export interface MaterialOption {
  id: number
  productName: string
  productCode?: string | null
  materialType: number
  categoryId?: number | null
  categoryName?: string | null
  price: number
}
