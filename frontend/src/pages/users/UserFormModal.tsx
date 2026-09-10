import { useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Eye, EyeOff, Save } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useAuth, useGroup, useLookups, useMe } from '@/lib/auth'
import { isSystemAdmin } from '@/lib/access'
import { formatPhone, isValidEmail, isValidPhone } from '@/lib/format'
import { Checkbox, ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { useToast } from '@/components/toast'
import { MultiSelect } from './MultiSelect'
import { PRIVILEGED_ROLES, ROLE_LABELS, type UserRow } from './types'

interface FormState {
  groupId: number | null
  email: string
  username: string
  password: string
  confirm: string
  firstName: string
  lastName: string
  phoneNumber: string
  roles: string[]
  extraGroupIds: number[]
}

type Errors = Partial<Record<keyof FormState, string>>

export function UserFormModal({ user, defaultGroupId, onClose }: { user: UserRow | null; defaultGroupId: number; onClose: () => void }) {
  const isNew = !user
  const me = useMe()
  const { refresh } = useAuth()
  const { groups } = useGroup()
  const lookups = useLookups()
  const qc = useQueryClient()
  const toast = useToast()
  const sysAdmin = isSystemAdmin(me)

  const [form, setForm] = useState<FormState>({
    groupId: user?.groupId ?? defaultGroupId ?? null,
    email: user?.email ?? '',
    username: user?.username ?? '',
    password: '',
    confirm: '',
    firstName: user?.firstName ?? '',
    lastName: user?.lastName ?? '',
    phoneNumber: user?.phoneNumber ?? '',
    roles: user?.roles ?? [],
    extraGroupIds: user?.extraGroupIds ?? [],
  })
  const [submitted, setSubmitted] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const [showPw, setShowPw] = useState(false)

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }))

  const roleOptions = useMemo(
    () => lookups.data?.roles ?? Object.entries(ROLE_LABELS).map(([value, label]) => ({ value, label })),
    [lookups.data],
  )
  const groupOptions = groups.map((g) => ({ value: g.id, label: g.name }))
  const extraOptions = groupOptions.filter((g) => g.value !== form.groupId)

  const errors = useMemo<Errors>(() => {
    const e: Errors = {}
    if (!form.groupId) e.groupId = 'Group is required.'
    if (!form.email.trim()) e.email = 'Email Address is required.'
    else if (!isValidEmail(form.email.trim())) e.email = 'Not a valid email address.'
    if (!form.username.trim()) e.username = 'Username is required.'
    else if (/\s/.test(form.username.trim())) e.username = 'Username cannot contain spaces.'
    if (isNew || form.password || form.confirm) {
      if (!form.password) e.password = 'New Password is required.'
      else if (form.password.length < 8) e.password = 'Password must be at least 8 characters.'
      if (form.password && form.confirm !== form.password) e.confirm = 'Passwords do not match.'
    }
    if (!isValidPhone(form.phoneNumber)) e.phoneNumber = 'Not a valid phone number'
    if (form.roles.length === 0) e.roles = 'Select at least one user role.'
    return e
  }, [form, isNew])
  const shown = (k: keyof FormState) => (submitted ? errors[k] : undefined)
  const cls = (k: keyof FormState) => (shown(k) ? 'input input-invalid' : 'input')

  const save = useMutation({
    mutationFn: () => {
      const body = {
        groupId: form.groupId,
        email: form.email.trim(),
        username: form.username.trim(),
        password: form.password || null,
        firstName: form.firstName.trim() || null,
        lastName: form.lastName.trim() || null,
        phoneNumber: form.phoneNumber || null,
        roles: form.roles,
        extraGroupIds: form.extraGroupIds.filter((g) => g !== form.groupId),
      }
      return isNew ? api.post('/users', body) : api.put(`/users/${user!.id}`, body)
    },
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['users'] })
      if (user?.isSelf) await refresh()
      onClose()
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setServerError(null)
    if (Object.keys(errors).length > 0) return
    save.mutate()
  }

  const toggleRole = (role: string, on: boolean) => set('roles', on ? [...form.roles, role] : form.roles.filter((r) => r !== role))
  const errorList = submitted ? Object.values(errors) : []
  const banner = serverError ?? (errorList.length ? errorList.join('\n') : null)
  const pwType = showPw ? 'text' : 'password'

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={isNew ? 'Create New User' : `Edit User — ${user!.username}`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>Cancel</button>
          <button className="btn-primary" type="submit" form="user-form" disabled={save.isPending}>
            {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
          </button>
        </>
      }
    >
      <form id="user-form" onSubmit={submit} noValidate autoComplete="off" className="space-y-4">
        <ErrorBanner message={banner} />
        <Field label="Group" required error={shown('groupId')}>
          <SearchSelect options={groupOptions} value={form.groupId} onChange={(v) => set('groupId', v)} clearable={false} invalid={!!shown('groupId')} placeholder="Select group" />
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Email Address" required error={shown('email')}>
            <input className={cls('email')} type="email" value={form.email} maxLength={256} onChange={(e) => set('email', e.target.value)} autoComplete="off" />
          </Field>
          <Field label="Username" required error={shown('username')}>
            <input className={cls('username')} value={form.username} maxLength={100} onChange={(e) => set('username', e.target.value)} autoComplete="off" />
          </Field>
          <Field label="New Password" required={isNew} error={shown('password')} hint={isNew ? 'At least 8 characters.' : 'Leave blank to keep the current password.'}>
            <div className="relative">
              <input className={cls('password') + ' pr-9'} type={pwType} value={form.password} onChange={(e) => set('password', e.target.value)} autoComplete="new-password" />
              <button type="button" className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground" onClick={() => setShowPw((s) => !s)} aria-label={showPw ? 'Hide password' : 'Show password'}>
                {showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </Field>
          <Field label="Repeat Password" required={isNew} error={shown('confirm')}>
            <input className={cls('confirm')} type={pwType} value={form.confirm} onChange={(e) => set('confirm', e.target.value)} autoComplete="new-password" />
          </Field>
          <Field label="First Name">
            <input className="input" value={form.firstName} maxLength={100} onChange={(e) => set('firstName', e.target.value)} />
          </Field>
          <Field label="Last Name">
            <input className="input" value={form.lastName} maxLength={100} onChange={(e) => set('lastName', e.target.value)} />
          </Field>
          <Field label="Phone Number" error={shown('phoneNumber')}>
            <input className={cls('phoneNumber')} type="tel" inputMode="tel" placeholder="(718) 697 - 9892" value={form.phoneNumber} onChange={(e) => set('phoneNumber', formatPhone(e.target.value))} />
          </Field>
        </div>

        <Field label="User Roles" required error={shown('roles')}>
          <div className={'grid gap-2 sm:grid-cols-2 rounded-md border p-3 ' + (shown('roles') ? 'border-destructive' : '')}>
            {roleOptions.map((r) => {
              const privileged = PRIVILEGED_ROLES.includes(r.value)
              const locked = !sysAdmin && privileged
              return (
                <div key={r.value} title={locked ? 'Only a System Administrator can assign this role.' : undefined}>
                  <Checkbox checked={form.roles.includes(r.value)} onChange={(v) => toggleRole(r.value, v)} disabled={locked} label={r.label} />
                </div>
              )
            })}
          </div>
        </Field>

        {extraOptions.length > 0 && (
          <Field label="Additional groups" hint="Optional — lets this user also work in other groups (they can switch groups in the header).">
            <MultiSelect options={extraOptions} value={form.extraGroupIds.filter((g) => g !== form.groupId)} onChange={(v) => set('extraGroupIds', v)} placeholder="None" />
          </Field>
        )}
      </form>
    </Modal>
  )
}
