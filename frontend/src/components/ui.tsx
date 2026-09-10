import { useEffect, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { AlertTriangle, Inbox, Loader2, Search, X } from 'lucide-react'
import clsx from 'clsx'

// ---------------------------------------------------------------------------
// Page chrome
// ---------------------------------------------------------------------------

export function PageHeader({
  title,
  breadcrumbs = [],
  actions,
  subtitle,
}: {
  title: ReactNode
  /** Crumbs after "Home"; strings or [label, href]. The last one is the current page. */
  breadcrumbs?: (string | [string, string])[]
  actions?: ReactNode
  subtitle?: ReactNode
}) {
  return (
    <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <nav className="text-xs text-muted-foreground mb-1 no-print">
          <Link to="/" className="hover:text-primary">
            Home
          </Link>
          {breadcrumbs.map((b, i) => (
            <span key={i}>
              {' / '}
              {Array.isArray(b) ? (
                <Link to={b[1]} className="hover:text-primary">
                  {b[0]}
                </Link>
              ) : (
                <span className={i === breadcrumbs.length - 1 ? 'text-foreground' : ''}>{b}</span>
              )}
            </span>
          ))}
        </nav>
        <h1 className="text-2xl font-semibold tracking-tight truncate">{title}</h1>
        {subtitle && <div className="text-sm text-muted-foreground mt-0.5">{subtitle}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 no-print">{actions}</div>}
    </div>
  )
}

export function Card({ title, actions, children, className, bodyClassName }: {
  title?: ReactNode
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}) {
  return (
    <section className={clsx('card', className)}>
      {(title || actions) && (
        <div className="flex items-center justify-between gap-2 border-b px-4 py-3">
          <h2 className="font-semibold">{title}</h2>
          {actions && <div className="flex items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={clsx('p-4', bodyClassName)}>{children}</div>
    </section>
  )
}

export function Tabs<K extends string>({
  tabs,
  value,
  onChange,
  className,
}: {
  tabs: { key: K; label: ReactNode; count?: number; hidden?: boolean }[]
  value: K
  onChange: (k: K) => void
  className?: string
}) {
  return (
    <div className={clsx('flex gap-1 border-b overflow-x-auto no-print', className)}>
      {tabs
        .filter((t) => !t.hidden)
        .map((t) => (
          <button key={t.key} type="button" className={clsx('tab', value === t.key && 'tab-active')} onClick={() => onChange(t.key)}>
            {t.label}
            {t.count !== undefined && <span className="ml-1.5 badge bg-muted text-muted-foreground">{t.count}</span>}
          </button>
        ))}
    </div>
  )
}

export function StatCard({ label, value, hint, icon, tone = 'default' }: {
  label: string
  value: ReactNode
  hint?: ReactNode
  icon?: ReactNode
  tone?: 'default' | 'primary' | 'success' | 'danger'
}) {
  return (
    <div className="card p-4">
      <div className="flex items-center justify-between text-muted-foreground text-xs font-medium uppercase tracking-wide">
        {label}
        {icon}
      </div>
      <div
        className={clsx(
          'mt-2 text-2xl font-semibold',
          tone === 'primary' && 'text-primary',
          tone === 'success' && 'text-success',
          tone === 'danger' && 'text-destructive',
        )}
      >
        {value}
      </div>
      {hint && <div className="mt-1 text-xs text-muted-foreground">{hint}</div>}
    </div>
  )
}

export function EmptyState({ title, description, action, icon }: {
  title: string
  description?: ReactNode
  action?: ReactNode
  icon?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-12 px-4">
      <div className="h-11 w-11 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-3">
        {icon ?? <Inbox className="h-5 w-5" />}
      </div>
      <div className="font-medium">{title}</div>
      {description && <div className="text-sm text-muted-foreground mt-1 max-w-md">{description}</div>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={clsx('h-4 w-4 animate-spin', className)} />
}

export function LoadingBlock({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-muted-foreground">
      <Spinner /> {label}
    </div>
  )
}

export function ErrorBanner({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <div className="mb-3 flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
      <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
      <span className="whitespace-pre-line">{message}</span>
    </div>
  )
}

export function Note({ children, tone = 'warning' }: { children: ReactNode; tone?: 'warning' | 'info' }) {
  return (
    <div
      className={clsx(
        'flex items-start gap-2 rounded-md border px-3 py-2 text-sm',
        tone === 'warning' ? 'border-amber-300 bg-amber-50 text-amber-900' : 'border-sky-200 bg-sky-50 text-sky-900',
      )}
    >
      <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
      <div>{children}</div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Forms
// ---------------------------------------------------------------------------

export function Field({ label, required, hint, error, children, className }: {
  label: ReactNode
  required?: boolean
  hint?: ReactNode
  error?: string | null
  children: ReactNode
  className?: string
}) {
  return (
    <div className={className}>
      <label className="label">
        {label}
        {required && <span className="text-destructive"> *</span>}
      </label>
      {children}
      {error ? (
        <div className="mt-1 text-xs text-destructive">{error}</div>
      ) : hint ? (
        <div className="mt-1 text-xs text-muted-foreground">{hint}</div>
      ) : null}
    </div>
  )
}

export function SearchInput({ value, onChange, placeholder = 'Search…', className }: {
  value: string
  onChange: (v: string) => void
  placeholder?: string
  className?: string
}) {
  return (
    <div className={clsx('relative', className)}>
      <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
      <input className="input pl-8 pr-8" value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
      {value && (
        <button type="button" className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground" onClick={() => onChange('')} aria-label="Clear search">
          <X className="h-4 w-4" />
        </button>
      )}
    </div>
  )
}

export function Checkbox({ checked, onChange, label, disabled }: {
  checked: boolean
  onChange: (v: boolean) => void
  label?: ReactNode
  disabled?: boolean
}) {
  return (
    <label className={clsx('inline-flex items-center gap-2 text-sm select-none', disabled ? 'opacity-60' : 'cursor-pointer')}>
      <input type="checkbox" className="h-4 w-4 rounded border-input accent-[hsl(var(--primary))]" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  )
}

// ---------------------------------------------------------------------------
// Dialogs
// ---------------------------------------------------------------------------

const sizes = { sm: 'max-w-md', md: 'max-w-xl', lg: 'max-w-3xl', xl: 'max-w-5xl', full: 'max-w-[96vw]' }

// Open modals in opening order; Escape only closes the topmost one (nested modals are common).
const modalStack: number[] = []
let modalSeq = 0

export function Modal({ open, onClose, title, children, footer, size = 'md', closeOnBackdrop = false }: {
  open: boolean
  onClose: () => void
  title: ReactNode
  children: ReactNode
  footer?: ReactNode
  size?: keyof typeof sizes
  closeOnBackdrop?: boolean
}) {
  const closeRef = useRef(onClose)
  closeRef.current = onClose
  useEffect(() => {
    if (!open) return
    const id = ++modalSeq
    modalStack.push(id)
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && modalStack[modalStack.length - 1] === id) closeRef.current()
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
      const i = modalStack.indexOf(id)
      if (i >= 0) modalStack.splice(i, 1)
    }
  }, [open])

  if (!open) return null
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-950/50 backdrop-blur-[2px] p-4 sm:p-8 no-print"
      onMouseDown={(e) => closeOnBackdrop && e.target === e.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" className={clsx('w-full card shadow-2xl my-auto', sizes[size])}>
        <div className="flex items-center justify-between gap-3 border-b px-5 py-3.5 border-b-2 border-b-primary/70">
          <h3 className="text-base font-semibold">{title}</h3>
          <button className="btn-icon" onClick={onClose} aria-label="Close">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="px-5 py-4 max-h-[75vh] overflow-y-auto">{children}</div>
        {footer && <div className="flex flex-wrap items-center justify-end gap-2 border-t px-5 py-3 bg-muted/40 rounded-b-lg">{footer}</div>}
      </div>
    </div>,
    document.body,
  )
}

export function ConfirmDialog({ open, title = 'Delete Confirmation', message, confirmLabel = 'Yes', onConfirm, onClose, busy, danger = true }: {
  open: boolean
  title?: string
  message: ReactNode
  confirmLabel?: string
  onConfirm: () => void
  onClose: () => void
  busy?: boolean
  danger?: boolean
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className={danger ? 'btn-danger' : 'btn-primary'} onClick={onConfirm} disabled={busy}>
            {busy && <Spinner />} {confirmLabel}
          </button>
        </>
      }
    >
      <p className="text-sm">{message}</p>
    </Modal>
  )
}
