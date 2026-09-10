import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { formatPhone, isValidEmail, isValidPhone } from '@/lib/format'
import { SearchSelect } from '@/components/SearchSelect'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { useVendors, type MessageResponse, type Vendor } from './shared'

type Form = Omit<Vendor, 'id' | 'groupName' | 'country'>

const blank = (groupId: number): Form => ({
  groupId,
  vendorName: '',
  address: '',
  city: '',
  state: '',
  zip: '',
  paymentTerms: '',
  accountNumber: '',
  contactName: '',
  officePhone: '',
  mobilePhone: '',
  vendorEmail: '',
  requestorEmail: '',
})

const fromVendor = (v: Vendor): Form => ({
  groupId: v.groupId,
  vendorName: v.vendorName,
  address: v.address ?? '',
  city: v.city ?? '',
  state: v.state ?? '',
  zip: v.zip ?? '',
  paymentTerms: v.paymentTerms ?? '',
  accountNumber: v.accountNumber ?? '',
  contactName: v.contactName ?? '',
  officePhone: v.officePhone ?? '',
  mobilePhone: v.mobilePhone ?? '',
  vendorEmail: v.vendorEmail ?? '',
  requestorEmail: v.requestorEmail ?? '',
})

/** "Vendor Setup": pick an existing vendor to edit (optional) or fill the form to create one. */
export function VendorSetupModal({ open, onClose, vendorId }: { open: boolean; onClose: () => void; vendorId?: number | null }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { groupId, groups } = useGroup()
  const vendors = useVendors(groupId)
  const [editing, setEditing] = useState<number | null>(null)
  const [f, setF] = useState<Form>(blank(groupId))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    const v = vendorId ? vendors.data?.find((x) => x.id === vendorId) : undefined
    setEditing(v?.id ?? null)
    setF(v ? fromVendor(v) : blank(groupId))
    setErrors({})
    setServerError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, vendorId, groupId])

  const pick = (id: number | null) => {
    setEditing(id)
    const v = vendors.data?.find((x) => x.id === id)
    setF(v ? fromVendor(v) : blank(groupId))
    setErrors({})
    setServerError(null)
  }

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setF((s) => ({ ...s, [k]: v }))

  const save = useMutation({
    mutationFn: () => {
      const body = Object.fromEntries(Object.entries(f).map(([k, v]) => [k, typeof v === 'string' ? v.trim() || null : v]))
      return (editing ? api.put<MessageResponse>(`/vendors/${editing}`, body) : api.post<MessageResponse>('/vendors', body)).then((r) => r.data)
    },
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['vendors'] })
      qc.invalidateQueries({ queryKey: ['materials'] })
      onClose()
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const submit = () => {
    const errs: Record<string, string> = {}
    if (!f.groupId) errs.groupId = 'Group is required.'
    if (!f.vendorName?.trim()) errs.vendorName = 'Vendor Name is required.'
    if (!isValidPhone(f.officePhone)) errs.officePhone = 'Not a valid phone number'
    if (!isValidPhone(f.mobilePhone)) errs.mobilePhone = 'Not a valid phone number'
    if (f.vendorEmail && !isValidEmail(f.vendorEmail)) errs.vendorEmail = 'Not a valid email address'
    if (f.requestorEmail && !isValidEmail(f.requestorEmail)) errs.requestorEmail = 'Not a valid email address'
    setErrors(errs)
    if (Object.keys(errs).length) {
      setServerError(Object.values(errs)[0])
      return
    }
    setServerError(null)
    save.mutate()
  }

  const text = (k: keyof Form, label: string, opts: { required?: boolean; phone?: boolean; type?: string; className?: string } = {}) => (
    <Field label={label} required={opts.required} error={errors[k]} className={opts.className}>
      <input
        className={clsx('input', errors[k] && 'input-invalid')}
        type={opts.type ?? 'text'}
        value={String(f[k] ?? '')}
        placeholder={opts.phone ? '(718) 697 - 9892' : undefined}
        onChange={(e) => set(k, (opts.phone ? formatPhone(e.target.value) : e.target.value) as never)}
        maxLength={400}
      />
    </Field>
  )

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Vendor Setup"
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>
            {save.isPending && <Spinner />} {editing ? 'Update Vendor' : 'Create Vendor'}
          </button>
        </>
      }
    >
      <ErrorBanner message={serverError} />
      <Field label="Edit Existing Vendor (optional)" className="mb-4" hint={editing ? 'Clear the selection to create a new vendor instead.' : 'Leave empty to create a new vendor.'}>
        <SearchSelect
          options={(vendors.data ?? []).map((v) => ({ value: v.id, label: v.vendorName, sub: [v.contactName, v.city].filter(Boolean).join(' · ') || undefined }))}
          value={editing}
          onChange={pick}
          placeholder={vendors.isLoading ? 'Loading…' : 'Select a vendor to edit'}
        />
      </Field>
      <div className="grid gap-4 sm:grid-cols-2 border-t pt-4">
        <Field label="Group" required error={errors.groupId}>
          <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={f.groupId} clearable={false} onChange={(v) => v && set('groupId', v)} />
        </Field>
        {text('vendorName', 'Vendor Name', { required: true })}
        {text('address', 'Address', { className: 'sm:col-span-2' })}
        <div className="grid grid-cols-3 gap-3 sm:col-span-2">
          {text('city', 'City')}
          {text('state', 'State')}
          {text('zip', 'Zip')}
        </div>
        {text('paymentTerms', 'Payment Terms')}
        {text('accountNumber', 'Account #')}
        {text('contactName', 'Contact Name', { className: 'sm:col-span-2' })}
        {text('officePhone', 'Office Phone', { phone: true })}
        {text('mobilePhone', 'Mobile Phone', { phone: true })}
        {text('vendorEmail', 'Vendor Email', { type: 'email' })}
        {text('requestorEmail', 'Requestor Email', { type: 'email' })}
      </div>
    </Modal>
  )
}
