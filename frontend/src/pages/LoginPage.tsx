import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Eye, EyeOff, LogIn } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
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

  // The server picks the database from the address the site was opened on (Production hosts vs. anything else).
  const { data: database } = useQuery({
    queryKey: ['auth-database'],
    queryFn: () => api.get<DatabaseRef>('/auth/database').then((r) => r.data),
    staleTime: Infinity,
  })

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
      await login(username.trim(), password, remember)
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
          {database?.production && (
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
