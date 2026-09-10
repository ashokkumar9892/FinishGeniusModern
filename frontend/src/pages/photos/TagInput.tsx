import { useEffect, useMemo, useRef, useState } from 'react'
import { Tag, X } from 'lucide-react'
import clsx from 'clsx'

export interface TagCount {
  tag: string
  count: number
}

const clean = (s: string) => s.trim().replace(/\s+/g, ' ')

/**
 * Chip input with suggestions. Enter or comma adds a tag, Backspace on an empty box removes the last one.
 * With `allowNew = false` only suggested tags can be chosen (used by the gallery filter).
 */
export function TagInput({ value, onChange, suggestions, placeholder = 'Add tags…', allowNew = true, className }: {
  value: string[]
  onChange: (tags: string[]) => void
  suggestions: TagCount[]
  placeholder?: string
  allowNew?: boolean
  className?: string
}) {
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const ref = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const chosen = useMemo(() => new Set(value.map((v) => v.toLowerCase())), [value])
  const matches = useMemo(() => {
    const q = clean(text).toLowerCase()
    return suggestions.filter((s) => !chosen.has(s.tag.toLowerCase()) && s.tag.toLowerCase().includes(q)).slice(0, 50)
  }, [suggestions, chosen, text])

  useEffect(() => setActive(0), [text, open])
  useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  const addMany = (raw: string[]) => {
    const next = [...value]
    const seen = new Set(chosen)
    for (const r of raw) {
      let tag = clean(r)
      if (!tag || seen.has(tag.toLowerCase())) continue
      const known = suggestions.find((s) => s.tag.toLowerCase() === tag.toLowerCase())
      if (known) tag = known.tag // keep the existing spelling/casing
      else if (!allowNew) continue
      seen.add(tag.toLowerCase())
      next.push(tag)
    }
    if (next.length !== value.length) onChange(next)
    setText('')
  }

  const remove = (tag: string) => onChange(value.filter((v) => v !== tag))

  return (
    <div ref={ref} className={clsx('relative', className)}>
      <div
        className="input flex h-auto min-h-9 cursor-text flex-wrap items-center gap-1 py-1"
        onClick={() => {
          inputRef.current?.focus()
          setOpen(true)
        }}
      >
        {value.map((t) => (
          <span key={t} className="badge gap-1 bg-primary/10 py-1 text-primary">
            <Tag className="h-3 w-3" />
            {t}
            <button
              type="button"
              className="rounded-full hover:text-destructive"
              onClick={(e) => {
                e.stopPropagation()
                remove(t)
              }}
              aria-label={`Remove tag ${t}`}
            >
              <X className="h-3 w-3" />
            </button>
          </span>
        ))}
        <input
          ref={inputRef}
          className="min-w-[8rem] flex-1 bg-transparent py-0.5 text-sm outline-none placeholder:text-muted-foreground"
          value={text}
          placeholder={value.length ? '' : placeholder}
          onFocus={() => setOpen(true)}
          onChange={(e) => {
            const v = e.target.value
            if (v.includes(',')) {
              const parts = v.split(',')
              addMany(parts.slice(0, -1))
              setText(parts[parts.length - 1])
            } else setText(v)
            setOpen(true)
          }}
          onBlur={() => allowNew && text.trim() && addMany([text])}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              if (open && matches[active] && (!text.trim() || !allowNew || matches[active].tag.toLowerCase().startsWith(clean(text).toLowerCase())))
                addMany([matches[active].tag])
              else addMany([text])
            } else if (e.key === 'Backspace' && !text && value.length) remove(value[value.length - 1])
            else if (e.key === 'ArrowDown') {
              e.preventDefault()
              setOpen(true)
              setActive((a) => Math.min(a + 1, matches.length - 1))
            } else if (e.key === 'ArrowUp') {
              e.preventDefault()
              setActive((a) => Math.max(a - 1, 0))
            } else if (e.key === 'Escape') setOpen(false)
          }}
        />
      </div>
      {open && (matches.length > 0 || (allowNew && clean(text))) && (
        <ul className="absolute z-40 mt-1 max-h-60 w-full overflow-y-auto card p-1 shadow-xl">
          {allowNew && clean(text) && !suggestions.some((s) => s.tag.toLowerCase() === clean(text).toLowerCase()) && (
            <li
              className="cursor-pointer rounded px-2 py-1.5 text-sm hover:bg-muted"
              onMouseDown={(e) => {
                e.preventDefault()
                addMany([text])
              }}
            >
              Add “{clean(text)}”
            </li>
          )}
          {matches.map((s, i) => (
            <li
              key={s.tag}
              onMouseEnter={() => setActive(i)}
              onMouseDown={(e) => {
                e.preventDefault()
                addMany([s.tag])
              }}
              className={clsx('flex cursor-pointer items-center justify-between gap-2 rounded px-2 py-1.5 text-sm', i === active && 'bg-muted')}
            >
              <span className="truncate">{s.tag}</span>
              <span className="text-xs text-muted-foreground">{s.count}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
