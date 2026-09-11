import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import clsx from 'clsx'
import { Database, Eye, EyeOff, LogIn } from 'lucide-react'
import { api, databaseStore, errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import type { DatabaseRef } from '@/lib/types'
import { Checkbox, ErrorBanner, Field, Note, Spinner } from '@/components/ui'
import { Logo } from '@/components/Layout'

export default function LoginPage() {
  const { me, login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [picked, setPicked] = useState(() => databaseStore.get())

  const { data: dbOptions } = useQuery({
    queryKey: ['auth-databases'],
    queryFn: () => api.get<{ databases: DatabaseRef[]; default: string }>('/auth/databases').then((r) => r.data),
    staleTime: Infinity,
  })
  const databases = dbOptions?.databases ?? []
  const database = databases.find((d) => d.key === picked) ?? databases.find((d) => d.key === dbOptions?.default) ?? databases[0]

  if (me) return <Navigate to="/" replace />

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!username.trim() || !password) {
      setError('Enter your username or email address and password.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await login(username.trim(), password, remember, database?.key)
      navigate('/', { replace: true })
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-h-full grid lg:grid-cols-2">
      <div className="hidden lg:flex flex-col justify-between bg-sidebar text-sidebar-foreground p-10 relative overflow-hidden">
        <div className="absolute -top-24 -right-24 h-96 w-96 rounded-full bg-primary/30 blur-3xl" />
        <div className="absolute bottom-0 left-0 h-72 w-72 rounded-full bg-primary/10 blur-3xl" />
        <div className="relative">
          <Logo size="lg" />
        </div>
        <div className="relative">
          <h2 className="text-3xl font-semibold leading-tight text-white">
            Industrial coatings formulation,
            <br /> process and inventory — in one place.
          </h2>
          <p className="mt-4 text-sidebar-muted max-w-md">
            Build process steps and schedules, estimate material quantities and pricing, run work on the shop floor and keep work
            instructions controlled.
          </p>
        </div>
        <div className="relative text-xs text-sidebar-muted">© AWFI Coating Finishing Solutions</div>
      </div>

      <div className="flex items-center justify-center p-6">
        <form onSubmit={submit} className="w-full max-w-sm space-y-5">
          <div className="lg:hidden mb-6">
            <Logo />
          </div>
          <div>
            <h1 className="text-2xl font-semibold">Sign in</h1>
            <p className="text-sm text-muted-foreground mt-1">Welcome back to Finish Genius.</p>
          </div>
          <ErrorBanner message={error} />
          {databases.length > 1 && (
            <Field label="Database">
              <div role="radiogroup" className="grid gap-2" style={{ gridTemplateColumns: `repeat(${databases.length}, minmax(0, 1fr))` }}>
                {databases.map((d) => {
                  const active = d.key === database?.key
                  return (
                    <button
                      key={d.key}
                      type="button"
                      role="radio"
                      aria-checked={active}
                      onClick={() => setPicked(d.key)}
                      className={clsx(
                        'h-9 rounded-md border text-sm font-medium inline-flex items-center justify-center gap-1.5 transition-colors',
                        !active && 'border-input bg-card text-muted-foreground hover:bg-muted',
                        active && !d.production && 'border-primary bg-primary/10 text-primary',
                        active && d.production && 'border-amber-500 bg-amber-500/15 text-amber-700 dark:text-amber-300',
                      )}
                    >
                      <Database className="h-4 w-4" /> {d.label}
                    </button>
                  )
                })}
              </div>
            </Field>
          )}
          {databases.length > 1 && database?.production && (
            <Note>You are signing in to the {database.label} database — changes affect live data.</Note>
          )}
          <Field label="Username or email address">
            <input className="input" autoFocus autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} placeholder="name@company.com" />
          </Field>
          <Field label="Password">
            <div className="relative">
              <input className="input pr-9" type={show ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••••" />
              <button type="button" className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground" onClick={() => setShow((s) => !s)} aria-label="Show password">
                {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </Field>
          <Checkbox checked={remember} onChange={setRemember} label="Remember me" />
          <button className="btn-primary w-full h-10" disabled={busy}>
            {busy ? <Spinner /> : <LogIn className="h-4 w-4" />} Sign in
          </button>
          <p className="text-xs text-muted-foreground text-center">Forgot your password? Ask your group administrator to reset it.</p>
        </form>
      </div>
    </div>
  )
}
