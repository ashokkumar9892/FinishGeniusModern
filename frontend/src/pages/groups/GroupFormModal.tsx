import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Copy, ImageOff, KeyRound, Save, Trash2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { useAuth, useLookups } from '@/lib/auth'
import { Checkbox, ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { FileDrop } from '@/components/FileDrop'
import { useToast } from '@/components/toast'
import { DEFAULT_TIME_ZONE, type GroupRow } from './types'

const IMAGE_EXT = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp']

interface FormState {
  name: string
  address1: string
  address2: string
  city: string
  state: string
  zip: string
  country: string
  timeZone: string
  checklistDeletionEnabled: boolean
}

/**
 * Create New Group / Edit Group / read-only group details (for users who may not edit).
 * Create only asks for the legacy fields (Full Name, Logo, checklist deletion); edit shows the full form.
 */
export function GroupFormModal({ group, readOnly, onClose }: { group: GroupRow | null; readOnly?: boolean; onClose: () => void }) {
  const isNew = !group
  const qc = useQueryClient()
  const toast = useToast()
  const { refresh } = useAuth()
  const lookups = useLookups()
  const [form, setForm] = useState<FormState>({
    name: group?.name ?? '',
    address1: group?.address1 ?? '',
    address2: group?.address2 ?? '',
    city: group?.city ?? '',
    state: group?.state ?? '',
    zip: group?.zip ?? '',
    country: group?.country ?? '',
    timeZone: group?.timeZone ?? DEFAULT_TIME_ZONE,
    checklistDeletionEnabled: group?.checklistDeletionEnabled ?? false,
  })
  const [logo, setLogo] = useState<File[]>([])
  const [removeLogo, setRemoveLogo] = useState(false)
  const [apiKey, setApiKey] = useState<string | null>(group?.apiKey ?? null)
  const [error, setError] = useState<string | null>(null)
  const [submitted, setSubmitted] = useState(false)

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }))
  const nameError = submitted && !form.name.trim() ? 'Full Name is required.' : null

  const newLogoUrl = useMemo(() => (logo[0] ? URL.createObjectURL(logo[0]) : null), [logo])
  useEffect(() => () => void (newLogoUrl && URL.revokeObjectURL(newLogoUrl)), [newLogoUrl])
  const previewUrl = newLogoUrl ?? (!removeLogo && group?.logoFile ? fileUrl(group.logoFile) : null)

  const tzOptions = useMemo(() => {
    const list = (lookups.data?.timeZones ?? []).map((z) => ({ value: z.value, label: z.label }))
    if (form.timeZone && !list.some((z) => z.value === form.timeZone)) list.unshift({ value: form.timeZone, label: form.timeZone })
    return list
  }, [lookups.data, form.timeZone])

  const save = useMutation({
    mutationFn: () => {
      const fd = new FormData()
      fd.append('name', form.name.trim())
      fd.append('checklistDeletionEnabled', String(form.checklistDeletionEnabled))
      if (!isNew) {
        for (const k of ['address1', 'address2', 'city', 'state', 'zip', 'country', 'timeZone'] as const) fd.append(k, form[k])
        fd.append('removeLogo', String(removeLogo))
      } else {
        fd.append('timeZone', DEFAULT_TIME_ZONE)
      }
      if (logo[0]) fd.append('logo', logo[0])
      return isNew ? api.post('/groups', fd) : api.put(`/groups/${group!.id}`, fd)
    },
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['groups'] })
      qc.invalidateQueries({ queryKey: ['lookups'] })
      await refresh() // new/renamed group shows in the header selector
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const genKey = useMutation({
    mutationFn: () => api.post<{ message: string; apiKey: string }>(`/groups/${group!.id}/api-key`),
    onSuccess: (res) => {
      setApiKey(res.data.apiKey)
      toast.success(`${res.data.message}\n${res.data.apiKey}`)
      qc.invalidateQueries({ queryKey: ['groups'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    if (!form.name.trim()) {
      setError('Full Name is required.')
      return
    }
    save.mutate()
  }

  const copyKey = async () => {
    if (!apiKey) return
    try {
      await navigator.clipboard.writeText(apiKey)
      toast.info('API Key copied to clipboard.')
    } catch {
      toast.error('Could not copy to the clipboard.')
    }
  }

  const title = isNew ? 'Create New Group' : readOnly ? 'Group Details' : 'Edit Group'
  const dis = readOnly || save.isPending
  const text = (k: 'address1' | 'address2' | 'city' | 'state' | 'zip' | 'country', label: string, max = 200) => (
    <Field label={label}>
      <input className="input" value={form[k]} maxLength={max} disabled={dis} onChange={(e) => set(k, e.target.value)} />
    </Field>
  )

  return (
    <Modal
      open
      onClose={onClose}
      title={title}
      size={isNew ? 'md' : 'lg'}
      footer={
        readOnly ? (
          <button className="btn-secondary" onClick={onClose}>Close</button>
        ) : (
          <>
            <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>Cancel</button>
            <button className="btn-primary" type="submit" form="group-form" disabled={save.isPending}>
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        )
      }
    >
      <form id="group-form" onSubmit={submit} noValidate className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="Full Name" required={!readOnly} error={nameError}>
          <input
            className={nameError ? 'input input-invalid' : 'input'}
            value={form.name}
            maxLength={200}
            autoFocus={!readOnly}
            disabled={dis}
            onChange={(e) => set('name', e.target.value)}
          />
        </Field>

        {!isNew && (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              {text('address1', 'Address1')}
              {text('city', 'City')}
              {text('address2', 'Address2')}
              {text('state', 'State', 100)}
              {text('zip', 'Zip', 20)}
              {text('country', 'Country', 100)}
            </div>
            <Field label="Time Zone">
              <SearchSelect
                options={tzOptions}
                value={form.timeZone}
                onChange={(v) => set('timeZone', v ?? DEFAULT_TIME_ZONE)}
                clearable={false}
                disabled={dis}
                placeholder={lookups.isLoading ? 'Loading time zones…' : 'Select time zone'}
              />
            </Field>
            {group?.canEdit && (
              <Field label="Api Key" hint="Used by devices and integrations to post data to this group. Generating a new key invalidates the old one.">
                <div className="flex gap-2">
                  <input className="input font-mono text-xs" readOnly value={apiKey ?? ''} placeholder="API Key not generated" />
                  {apiKey && (
                    <button type="button" className="btn-secondary px-2.5" onClick={copyKey} title="Copy API Key">
                      <Copy className="h-4 w-4" />
                    </button>
                  )}
                  {!readOnly && (
                    <button type="button" className="btn-secondary shrink-0" onClick={() => genKey.mutate()} disabled={genKey.isPending}>
                      {genKey.isPending ? <Spinner /> : <KeyRound className="h-4 w-4" />} Generate API Key
                    </button>
                  )}
                </div>
              </Field>
            )}
          </>
        )}

        <Field label="Logo Image">
          <div className="flex flex-col sm:flex-row gap-3">
            {(previewUrl || !isNew) && (
              <div className="shrink-0 flex flex-col items-center gap-1.5">
                <div className="h-24 w-24 rounded-md border bg-white grid place-items-center overflow-hidden">
                  {previewUrl ? (
                    <img src={previewUrl} alt="Group logo" className="max-h-full max-w-full object-contain" />
                  ) : (
                    <ImageOff className="h-6 w-6 text-muted-foreground" />
                  )}
                </div>
                {!readOnly && !isNew && group?.logoFile && !removeLogo && !logo[0] && (
                  <button type="button" className="btn-ghost btn-sm text-destructive" onClick={() => setRemoveLogo(true)}>
                    <Trash2 className="h-3.5 w-3.5" /> Remove
                  </button>
                )}
              </div>
            )}
            {!readOnly && (
              <div className="flex-1 min-w-0">
                <FileDrop
                  files={logo}
                  onChange={(f) => {
                    setLogo(f)
                    if (f.length) setRemoveLogo(false)
                  }}
                  accept={IMAGE_EXT}
                  label={isNew || !group?.logoFile ? 'Drag & drop a logo image here …' : 'Drag & drop a new logo to replace the current one …'}
                />
              </div>
            )}
          </div>
        </Field>

        <Checkbox
          checked={form.checklistDeletionEnabled}
          onChange={(v) => set('checklistDeletionEnabled', v)}
          disabled={dis}
          label="Enable Checklist Deletion on Submit"
        />
      </form>
    </Modal>
  )
}
