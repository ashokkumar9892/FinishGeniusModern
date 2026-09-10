import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, KeyRound, Save, XCircle } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { dateTime, formatPhone, isValidPhone } from '@/lib/format'
import { Card, ErrorBanner, Field, LoadingBlock, PageHeader, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'

interface Profile {
  id: number
  username: string
  email: string
  firstName?: string | null
  lastName?: string | null
  phoneNumber?: string | null
  groupId: number
  groupName?: string | null
  extraGroups: string[]
  roles: string[]
  roleLabels: string[]
  agreementAccepted: boolean
  agreementAcceptedAt?: string | null
  createdAt: string
  lastLoginAt?: string | null
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[120px_1fr] gap-3 py-2 border-b last:border-0 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children || <span className="text-muted-foreground">—</span>}</dd>
    </div>
  )
}

function ProfileForm({ profile }: { profile: Profile }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { refresh } = useAuth()
  const [firstName, setFirstName] = useState(profile.firstName ?? '')
  const [lastName, setLastName] = useState(profile.lastName ?? '')
  const [phone, setPhone] = useState(profile.phoneNumber ?? '')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setFirstName(profile.firstName ?? '')
    setLastName(profile.lastName ?? '')
    setPhone(profile.phoneNumber ?? '')
  }, [profile])

  const phoneError = !isValidPhone(phone) ? 'Not a valid phone number' : null
  const dirty = firstName !== (profile.firstName ?? '') || lastName !== (profile.lastName ?? '') || phone !== (profile.phoneNumber ?? '')

  const save = useMutation({
    mutationFn: () => api.put('/users/me', { firstName: firstName.trim() || null, lastName: lastName.trim() || null, phoneNumber: phone || null }),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['profile'] })
      await refresh()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    if (phoneError) {
      setError(phoneError)
      return
    }
    save.mutate()
  }

  return (
    <form onSubmit={submit} noValidate className="space-y-4">
      <ErrorBanner message={error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="First Name">
          <input className="input" value={firstName} maxLength={100} onChange={(e) => setFirstName(e.target.value)} />
        </Field>
        <Field label="Last Name">
          <input className="input" value={lastName} maxLength={100} onChange={(e) => setLastName(e.target.value)} />
        </Field>
        <Field label="Phone Number" error={phone && phoneError ? phoneError : null}>
          <input
            className={phone && phoneError ? 'input input-invalid' : 'input'}
            type="tel"
            inputMode="tel"
            placeholder="(718) 697 - 9892"
            value={phone}
            onChange={(e) => setPhone(formatPhone(e.target.value))}
          />
        </Field>
      </div>
      <div className="flex justify-end">
        <button className="btn-primary" type="submit" disabled={save.isPending || !dirty}>
          {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save changes
        </button>
      </div>
    </form>
  )
}

function ChangePasswordForm() {
  const toast = useToast()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const errors = {
    current: !current ? 'Current password is required.' : null,
    next: !next ? 'New password is required.' : next.length < 8 ? 'Password must be at least 8 characters.' : next === current ? 'The new password must be different from the current one.' : null,
    confirm: confirm !== next ? 'Passwords do not match.' : null,
  }
  const show = (k: keyof typeof errors) => (submitted ? errors[k] : null)

  const change = useMutation({
    mutationFn: () => api.post('/auth/change-password', { currentPassword: current, newPassword: next }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setCurrent('')
      setNext('')
      setConfirm('')
      setSubmitted(false)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    if (Object.values(errors).some(Boolean)) return
    change.mutate()
  }

  return (
    <form onSubmit={submit} noValidate className="space-y-4" autoComplete="off">
      <ErrorBanner message={error} />
      <Field label="Current Password" required error={show('current')}>
        <input className={show('current') ? 'input input-invalid' : 'input'} type="password" value={current} autoComplete="current-password" onChange={(e) => setCurrent(e.target.value)} />
      </Field>
      <Field label="New Password" required error={show('next')} hint="At least 8 characters.">
        <input className={show('next') ? 'input input-invalid' : 'input'} type="password" value={next} autoComplete="new-password" onChange={(e) => setNext(e.target.value)} />
      </Field>
      <Field label="Confirm New Password" required error={show('confirm')}>
        <input className={show('confirm') ? 'input input-invalid' : 'input'} type="password" value={confirm} autoComplete="new-password" onChange={(e) => setConfirm(e.target.value)} />
      </Field>
      <div className="flex justify-end">
        <button className="btn-primary" type="submit" disabled={change.isPending}>
          {change.isPending ? <Spinner /> : <KeyRound className="h-4 w-4" />} Change Password
        </button>
      </div>
    </form>
  )
}

export default function ProfilePage() {
  const profile = useQuery({
    queryKey: ['profile'],
    queryFn: () => api.get<Profile>('/users/me').then((r) => r.data),
  })
  const p = profile.data

  return (
    <>
      <PageHeader title="Profile" breadcrumbs={['Profile']} subtitle="Your account details and password" />
      {profile.isLoading ? (
        <LoadingBlock />
      ) : profile.isError || !p ? (
        <ErrorBanner message={errorMessage(profile.error)} />
      ) : (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]">
          <Card title="Account">
            <div className="flex items-center gap-3 mb-3">
              <span className="h-12 w-12 rounded-full bg-primary/15 text-primary grid place-items-center font-bold">
                {((p.firstName?.[0] ?? '') + (p.lastName?.[0] ?? '') || p.username[0]).toUpperCase()}
              </span>
              <div className="min-w-0">
                <div className="font-semibold truncate">{[p.firstName, p.lastName].filter(Boolean).join(' ') || p.username}</div>
                <div className="text-sm text-muted-foreground truncate">{p.email}</div>
              </div>
            </div>
            <dl>
              <Detail label="Username">{p.username}</Detail>
              <Detail label="Email">{p.email}</Detail>
              <Detail label="Phone">{p.phoneNumber}</Detail>
              <Detail label="Group">{p.groupName}</Detail>
              {p.extraGroups.length > 0 && <Detail label="Also in">{p.extraGroups.join(', ')}</Detail>}
              <Detail label="Roles">
                <div className="flex flex-wrap gap-1">
                  {p.roleLabels.map((r) => (
                    <span key={r} className="badge bg-muted">{r}</span>
                  ))}
                </div>
              </Detail>
              <Detail label="User agreement">
                {p.agreementAccepted ? (
                  <span className="inline-flex items-center gap-1.5 text-success">
                    <CheckCircle2 className="h-4 w-4" /> Accepted {dateTime(p.agreementAcceptedAt)}
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1.5 text-destructive">
                    <XCircle className="h-4 w-4" /> Not accepted
                  </span>
                )}
              </Detail>
              <Detail label="Last sign-in">{dateTime(p.lastLoginAt)}</Detail>
              <Detail label="Member since">{dateTime(p.createdAt)}</Detail>
            </dl>
          </Card>
          <div className="space-y-5">
            <Card title="Edit profile">
              <ProfileForm profile={p} />
            </Card>
            <Card title="Change Password">
              <ChangePasswordForm />
            </Card>
          </div>
        </div>
      )}
    </>
  )
}
