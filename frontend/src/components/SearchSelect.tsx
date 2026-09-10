import { useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Search, X } from 'lucide-react'
import clsx from 'clsx'
import type { Option } from '@/lib/types'

/**
 * Searchable single-select (the legacy app's Select2 dropdowns): used for Group, Process System,
 * materials, vendors… Pass `value = null` for "nothing selected".
 */
export function SearchSelect<T extends string | number>({
  options,
  value,
  onChange,
  placeholder = 'Select…',
  clearable = true,
  disabled,
  className,
  invalid,
  emptyText = 'No results found',
}: {
  options: Option<T>[]
  value: T | null | undefined
  onChange: (value: T | null) => void
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
  className?: string
  invalid?: boolean
  emptyText?: string
}) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const ref = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = options.find((o) => o.value === value)
  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    const list = s ? options.filter((o) => `${o.label} ${o.sub ?? ''}`.toLowerCase().includes(s)) : options
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

  const pick = (o: Option<T>) => {
    onChange(o.value)
    setOpen(false)
    setQ('')
  }

  return (
    <div ref={ref} className={clsx('relative', className)}>
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((o) => !o)}
        className={clsx('input flex items-center justify-between gap-2 text-left', invalid && 'input-invalid')}
      >
        <span className={clsx('truncate', !selected && 'text-muted-foreground')}>{selected?.label ?? placeholder}</span>
        <span className="flex items-center gap-1 shrink-0 text-muted-foreground">
          {clearable && selected && !disabled && (
            <X
              className="h-3.5 w-3.5 hover:text-foreground"
              onClick={(e) => {
                e.stopPropagation()
                onChange(null)
              }}
            />
          )}
          <ChevronDown className="h-4 w-4" />
        </span>
      </button>
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
                  pick(filtered[active])
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
                  pick(o)
                }}
                className={clsx('flex items-center gap-2 rounded px-2 py-1.5 text-sm cursor-pointer', i === active && 'bg-muted')}
              >
                <Check className={clsx('h-3.5 w-3.5 shrink-0 text-primary', o.value !== value && 'invisible')} />
                <span className="min-w-0">
                  <span className="block truncate">{o.label}</span>
                  {o.sub && <span className="block truncate text-xs text-muted-foreground">{o.sub}</span>}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
