import { useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Copy, Eye, EyeOff, KeyRound, Wand2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { ErrorBanner, Field, Modal, Note, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import type { UserRow } from './types'

const CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%*?'

/** Random 10-character password with at least one upper, lower, digit and symbol. */
function generatePassword(length = 10) {
  const groups = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@#$%*?']
  const rand = (n: number) => crypto.getRandomValues(new Uint32Array(1))[0] % n
  const chars = groups.map((g) => g[rand(g.length)])
  while (chars.length < length) chars.push(CHARS[rand(CHARS.length)])
  for (let i = chars.length - 1; i > 0; i--) {
    const j = rand(i + 1)
    ;[chars[i], chars[j]] = [chars[j], chars[i]]
  }
  return chars.join('')
}

export function ResetPasswordModal({ user, onClose }: { user: UserRow; onClose: () => void }) {
  const toast = useToast()
  const [password, setPassword] = useState('')
  const [show, setShow] = useState(false)
  const [generated, setGenerated] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reset = useMutation({
    mutationFn: () => api.post(`/users/${user.id}/reset-password`, { password }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    if (password.length < 8) {
      setError('Password must be at least 8 characters.')
      return
    }
    reset.mutate()
  }

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(password)
      toast.info('Password copied to clipboard.')
    } catch {
      toast.error('Could not copy to the clipboard.')
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title="Reset Password"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={reset.isPending}>Cancel</button>
          <button className="btn-primary" type="submit" form="reset-pw-form" disabled={reset.isPending}>
            {reset.isPending ? <Spinner /> : <KeyRound className="h-4 w-4" />} Reset Password
          </button>
        </>
      }
    >
      <form id="reset-pw-form" onSubmit={submit} noValidate className="space-y-4">
        <ErrorBanner message={error} />
        <p className="text-sm">
          Set a new password for <span className="font-semibold">{user.username}</span>
          {user.email && <span className="text-muted-foreground"> ({user.email})</span>}.
        </p>
        <Field label="New Password" required hint="At least 8 characters.">
          <div className="flex gap-2">
            <div className="relative flex-1">
              <input
                className="input pr-9 font-mono"
                type={show ? 'text' : 'password'}
                value={password}
                autoComplete="new-password"
                autoFocus
                onChange={(e) => {
                  setPassword(e.target.value)
                  setGenerated(false)
                }}
              />
              <button type="button" className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground" onClick={() => setShow((s) => !s)} aria-label={show ? 'Hide password' : 'Show password'}>
                {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
            {password && (
              <button type="button" className="btn-secondary px-2.5" onClick={copy} title="Copy password">
                <Copy className="h-4 w-4" />
              </button>
            )}
            <button
              type="button"
              className="btn-secondary"
              onClick={() => {
                setPassword(generatePassword())
                setShow(true)
                setGenerated(true)
              }}
            >
              <Wand2 className="h-4 w-4" /> Generate
            </button>
          </div>
        </Field>
        {generated && (
          <Note tone="info">Copy this password now and share it with the user securely — it will not be shown again after you close this dialog.</Note>
        )}
      </form>
    </Modal>
  )
}
