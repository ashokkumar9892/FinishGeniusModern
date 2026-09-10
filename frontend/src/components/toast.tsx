import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { CheckCircle2, Info, X, XCircle } from 'lucide-react'
import clsx from 'clsx'

type Kind = 'success' | 'error' | 'info'
interface Toast {
  id: number
  kind: Kind
  message: string
}

interface ToastApi {
  success: (message: string) => void
  error: (message: string) => void
  info: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)
let nextId = 1

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const push = useCallback((kind: Kind, message: string) => {
    const id = nextId++
    setToasts((t) => [...t, { id, kind, message }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), kind === 'error' ? 6000 : 3500)
  }, [])

  const api = useMemo<ToastApi>(
    () => ({
      success: (m) => push('success', m),
      error: (m) => push('error', m),
      info: (m) => push('info', m),
    }),
    [push],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="fixed top-3 left-1/2 -translate-x-1/2 z-[100] flex flex-col gap-2 w-[min(92vw,440px)] no-print">
        {toasts.map((t) => (
          <div
            key={t.id}
            role="status"
            className={clsx(
              'flex items-start gap-2 rounded-md px-4 py-3 text-sm shadow-lg text-white animate-in',
              t.kind === 'success' && 'bg-success',
              t.kind === 'error' && 'bg-destructive',
              t.kind === 'info' && 'bg-slate-800',
            )}
          >
            {t.kind === 'success' && <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0" />}
            {t.kind === 'error' && <XCircle className="h-4 w-4 mt-0.5 shrink-0" />}
            {t.kind === 'info' && <Info className="h-4 w-4 mt-0.5 shrink-0" />}
            <span className="flex-1 whitespace-pre-line">{t.message}</span>
            <button onClick={() => setToasts((x) => x.filter((y) => y.id !== t.id))} aria-label="Dismiss">
              <X className="h-4 w-4 opacity-80 hover:opacity-100" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast() {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used inside ToastProvider')
  return ctx
}
