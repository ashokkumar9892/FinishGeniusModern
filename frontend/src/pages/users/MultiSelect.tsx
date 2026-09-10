import { useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Search, X } from 'lucide-react'
import clsx from 'clsx'
import type { Option } from '@/lib/types'

/**
 * Searchable multi-select with removable chips. Used for a user's additional groups and message recipients.
 * Module-private (Groups/Users/Messages); options may carry a `sub` line (e.g. an email address).
 */
export function MultiSelect<T extends string | number>({
  options,
  value,
  onChange,
  placeholder = 'Select…',
  invalid,
  disabled,
  emptyText = 'No results found',
}: {
  options: (Option<T> & { tag?: string | null })[]
  value: T[]
  onChange: (value: T[]) => void
  placeholder?: string
  invalid?: boolean
  disabled?: boolean
  emptyText?: string
}) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const ref = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = useMemo(() => new Set(value), [value])
  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    const list = s ? options.filter((o) => `${o.label} ${o.sub ?? ''} ${o.tag ?? ''}`.toLowerCase().includes(s)) : options
    return list.slice(0, 300)
  }, [options, q])

  useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    setTimeout(() => inputRef.current?.focus(), 0)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  useEffect(() => setActive(0), [q, open])

  const toggle = (v: T) => onChange(selected.has(v) ? value.filter((x) => x !== v) : [...value, v])
  const chips = value.map((v) => options.find((o) => o.value === v)).filter((o): o is Option<T> & { tag?: string | null } => !!o)

  return (
    <div ref={ref} className="relative">
      <div
        role="button"
        tabIndex={disabled ? -1 : 0}
        onClick={() => !disabled && setOpen((o) => !o)}
        onKeyDown={(e) => {
          if (!disabled && (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown')) {
            e.preventDefault()
            setOpen(true)
          }
        }}
        className={clsx(
          'input h-auto min-h-9 flex flex-wrap items-center gap-1 py-1 pr-8 cursor-pointer relative',
          invalid && 'input-invalid',
          disabled && 'bg-muted cursor-not-allowed',
        )}
      >
        {chips.length === 0 && <span className="text-muted-foreground px-0.5">{placeholder}</span>}
        {chips.map((o) => (
          <span key={String(o.value)} className="badge bg-accent text-accent-foreground gap-1 max-w-full">
            <span className="truncate">{o.label}</span>
            {!disabled && (
              <button
                type="button"
                className="opacity-70 hover:opacity-100"
                aria-label={`Remove ${o.label}`}
                onClick={(e) => {
                  e.stopPropagation()
                  toggle(o.value)
                }}
              >
                <X className="h-3 w-3" />
              </button>
            )}
          </span>
        ))}
        <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
      </div>
      {open && (
        <div className="absolute z-40 mt-1 w-full min-w-[240px] card shadow-xl p-1">
          <div className="relative mb-1">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
            <input
              ref={inputRef}
              className="input h-8 pl-7"
              value={q}
              placeholder="Search…"
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'ArrowDown') setActive((a) => Math.min(a + 1, filtered.length - 1))
                else if (e.key === 'ArrowUp') setActive((a) => Math.max(a - 1, 0))
                else if (e.key === 'Enter' && filtered[active]) {
                  e.preventDefault()
                  toggle(filtered[active].value)
                } else if (e.key === 'Escape') setOpen(false)
              }}
            />
          </div>
          <ul className="max-h-64 overflow-y-auto">
            {filtered.length === 0 && <li className="px-2 py-2 text-sm text-muted-foreground">{emptyText}</li>}
            {filtered.map((o, i) => (
              <li
                key={String(o.value)}
                onMouseEnter={() => setActive(i)}
                onMouseDown={(e) => {
                  e.preventDefault()
                  toggle(o.value)
                }}
                className={clsx('flex items-center gap-2 rounded px-2 py-1.5 text-sm cursor-pointer', i === active && 'bg-muted')}
              >
                <span className={clsx('h-4 w-4 shrink-0 rounded border grid place-items-center', selected.has(o.value) ? 'bg-primary border-primary text-primary-foreground' : 'border-input')}>
                  {selected.has(o.value) && <Check className="h-3 w-3" />}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate">{o.label}</span>
                  {o.sub && <span className="block truncate text-xs text-muted-foreground">{o.sub}</span>}
                </span>
                {o.tag && <span className="badge bg-sky-100 text-sky-800 shrink-0">{o.tag}</span>}
              </li>
            ))}
          </ul>
          {value.length > 0 && (
            <div className="flex items-center justify-between border-t mt-1 px-2 pt-1.5 pb-0.5 text-xs text-muted-foreground">
              <span>{value.length} selected</span>
              <button type="button" className="hover:text-foreground" onMouseDown={(e) => { e.preventDefault(); onChange([]) }}>
                Clear
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
