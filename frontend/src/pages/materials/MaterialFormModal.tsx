import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, X } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { canAccess } from '@/lib/access'
import { MaterialType } from '@/lib/types'
import { SearchSelect } from '@/components/SearchSelect'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { editableTypes, parseNum, unitLabel, useCategories, useVendors, type Material, type MessageResponse } from './shared'

interface FormState {
  groupId: number
  materialType: number | null
  categoryId: number | null
  productCode: string
  productName: string
  density: string
  price: string
  voc: string
  hap: string
  tap: string
  minQuantity: string
  vendorId: number | null
  notes: string
}

const numericFields = [
  ['density', 'Density'],
  ['price', 'Price'],
  ['voc', 'VOC'],
  ['hap', 'HAP'],
  ['tap', 'TAP'],
  ['minQuantity', 'Min. Quantity'],
] as const

function initial(material: Material | null | undefined, groupId: number, defaultType?: number): FormState {
  const s = (n?: number | null) => (n == null ? '' : String(n))
  return material
    ? {
        groupId: material.groupId,
        materialType: material.materialType,
        categoryId: material.categoryId,
        productCode: material.productCode ?? '',
        productName: material.productName,
        density: s(material.density),
        price: s(material.price),
        voc: s(material.voc),
        hap: s(material.hap),
        tap: s(material.tap),
        minQuantity: s(material.minQuantity),
        vendorId: material.vendorId,
        notes: material.notes ?? '',
      }
    : {
        groupId,
        materialType: defaultType ?? MaterialType.Base,
        categoryId: null,
        productCode: '',
        productName: '',
        density: '',
        price: '',
        voc: '',
        hap: '',
        tap: '',
        minQuantity: '',
        vendorId: null,
        notes: '',
      }
}

/** "Create New Material" / "Edit Material" (+ Item, row Edit). */
export function MaterialFormModal({ open, onClose, material, defaultType }: {
  open: boolean
  onClose: () => void
  material?: Material | null
  defaultType?: number
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const me = useMe()
  const { groupId, groups } = useGroup()
  const [f, setF] = useState<FormState>(() => initial(material, groupId, defaultType))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [newCat, setNewCat] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setF(initial(material, groupId, defaultType))
      setErrors({})
      setServerError(null)
      setNewCat(null)
    }
  }, [open, material, groupId, defaultType])

  const categories = useCategories(f.groupId, f.materialType)
  const vendors = useVendors(f.groupId)
  const canCreateCategory = canAccess(me, 'materialCategories')
  const isEquipment = f.materialType === MaterialType.Equipment
  const unit = unitLabel(f.materialType ?? MaterialType.Base)

  // De-duplicated category options (legacy data can hold the same name twice).
  const categoryOptions = useMemo(() => {
    const seen = new Set<string>()
    return (categories.data ?? [])
      .filter((c) => {
        const k = c.name.trim().toLowerCase()
        if (seen.has(k) && c.id !== f.categoryId) return false
        seen.add(k)
        return true
      })
      .map((c) => ({ value: c.id, label: c.name, sub: c.filter2 ? `Filter: ${c.filter2}` : undefined }))
  }, [categories.data, f.categoryId])

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setF((s) => ({ ...s, [k]: v }))

  const createCategory = useMutation({
    mutationFn: (name: string) =>
      api.post<MessageResponse>('/material-categories', { groupId: f.groupId, name, materialType: f.materialType }).then((r) => r.data),
    onSuccess: async (res) => {
      toast.success(res.message)
      await qc.invalidateQueries({ queryKey: ['material-categories'] })
      if (res.id) set('categoryId', res.id)
      setNewCat(null)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const save = useMutation({
    mutationFn: (body: Record<string, unknown>) =>
      (material ? api.put<MessageResponse>(`/materials/${material.id}`, body) : api.post<MessageResponse>('/materials', body)).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['material-categories'] })
      qc.invalidateQueries({ queryKey: ['history', 'Material'] })
      onClose()
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const submit = () => {
    const errs: Record<string, string> = {}
    if (!f.groupId) errs.groupId = 'Group is required.'
    if (!f.materialType) errs.materialType = 'Material Type is required.'
    if (!f.productName.trim()) errs.productName = 'Product Name is required.'
    const nums: Record<string, number> = {}
    for (const [key, label] of numericFields) {
      if (isEquipment && (key === 'voc' || key === 'hap' || key === 'tap')) {
        nums[key] = 0
        continue
      }
      const n = parseNum(f[key])
      if (n === null) errs[key] = `${label} must be a number.`
      else if (n < 0) errs[key] = `${label} cannot be negative.`
      else nums[key] = n
    }
    setErrors(errs)
    if (Object.keys(errs).length) {
      setServerError('Please correct the highlighted fields.')
      return
    }
    setServerError(null)
    save.mutate({
      groupId: f.groupId,
      materialType: f.materialType,
      categoryId: f.categoryId,
      productCode: f.productCode.trim() || null,
      productName: f.productName.trim(),
      vendorId: f.vendorId,
      notes: f.notes.trim() || null,
      ...nums,
    })
  }

  const numInput = (key: (typeof numericFields)[number][0], label: string, placeholder = '0.00') => (
    <Field label={label} error={errors[key]}>
      <input
        className={clsx('input', errors[key] && 'input-invalid')}
        inputMode="decimal"
        value={f[key]}
        placeholder={placeholder}
        onChange={(e) => set(key, e.target.value)}
      />
    </Field>
  )

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={material ? 'Edit Material' : 'Create New Material'}
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <ErrorBanner message={serverError} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Group" required error={errors.groupId}>
          <SearchSelect
            options={groups.map((g) => ({ value: g.id, label: g.name }))}
            value={f.groupId}
            clearable={false}
            invalid={!!errors.groupId}
            onChange={(v) => setF((s) => ({ ...s, groupId: v ?? s.groupId, categoryId: v === s.groupId ? s.categoryId : null, vendorId: v === s.groupId ? s.vendorId : null }))}
          />
        </Field>
        <Field label="Material Type" required error={errors.materialType}>
          <select
            className={clsx('input', errors.materialType && 'input-invalid')}
            value={f.materialType ?? ''}
            onChange={(e) => {
              const t = Number(e.target.value) || null
              setF((s) => ({ ...s, materialType: t, categoryId: null }))
            }}
          >
            <option value="">Select…</option>
            {editableTypes.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </Field>

        <Field
          label={
            <span className="flex items-center justify-between gap-2">
              Material Category
              {canCreateCategory && f.materialType && newCat === null && (
                <button type="button" className="text-xs font-medium text-primary hover:underline inline-flex items-center gap-0.5" onClick={() => setNewCat('')}>
                  <Plus className="h-3 w-3" /> New category
                </button>
              )}
            </span>
          }
          className="sm:col-span-2"
          hint={!f.materialType ? 'Choose a Material Type first.' : categoryOptions.length === 0 && !categories.isLoading ? 'No categories exist for this type yet.' : undefined}
        >
          {newCat !== null ? (
            <div className="flex gap-2">
              <input
                className="input"
                autoFocus
                placeholder="New category name"
                value={newCat}
                onChange={(e) => setNewCat(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && newCat.trim()) {
                    e.preventDefault()
                    createCategory.mutate(newCat.trim())
                  }
                }}
              />
              <button type="button" className="btn-primary" disabled={!newCat.trim() || createCategory.isPending} onClick={() => createCategory.mutate(newCat.trim())}>
                {createCategory.isPending && <Spinner />} Add
              </button>
              <button type="button" className="btn-icon" title="Cancel" onClick={() => setNewCat(null)}>
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <SearchSelect
              options={categoryOptions}
              value={f.categoryId}
              onChange={(v) => set('categoryId', v)}
              disabled={!f.materialType}
              placeholder={categories.isLoading ? 'Loading…' : 'Select category'}
            />
          )}
        </Field>

        <Field label="Product Code">
          <input className="input" value={f.productCode} onChange={(e) => set('productCode', e.target.value)} maxLength={400} />
        </Field>
        <Field label="Product Name" required error={errors.productName}>
          <input
            className={clsx('input', errors.productName && 'input-invalid')}
            value={f.productName}
            onChange={(e) => set('productName', e.target.value)}
            maxLength={400}
          />
        </Field>

        {numInput('density', 'Density (lb/gal)')}
        {numInput('price', isEquipment || f.materialType === MaterialType.Sundry ? 'Price ($/pc)' : 'Price ($/gal)')}
        {!isEquipment && (
          <div className="grid grid-cols-3 gap-3 sm:col-span-2">
            {numInput('voc', 'VOC (lb/gal)')}
            {numInput('hap', 'HAP (lb/gal)')}
            {numInput('tap', 'TAP (lb/gal)')}
          </div>
        )}
        {numInput('minQuantity', `Min. Quantity (${unit})`, '0')}
        <Field label="Vendor" hint="Optional — pre-selects the vendor when reordering.">
          <SearchSelect
            options={(vendors.data ?? []).map((v) => ({ value: v.id, label: v.vendorName, sub: v.contactName ?? undefined }))}
            value={f.vendorId}
            onChange={(v) => set('vendorId', v)}
            placeholder={vendors.isLoading ? 'Loading…' : 'No vendor'}
          />
        </Field>
        <Field label="Notes" className="sm:col-span-2">
          <textarea className="input" rows={3} value={f.notes} onChange={(e) => set('notes', e.target.value)} maxLength={4000} />
        </Field>
      </div>
    </Modal>
  )
}
