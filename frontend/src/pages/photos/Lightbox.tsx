import { useEffect, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronLeft, ChevronRight, X } from 'lucide-react'

export interface LightboxItem {
  src: string
  title?: string
  caption?: ReactNode
}

/** Full-screen image viewer: prev/next buttons, ← / → keys, swipe on touch screens, Esc or backdrop click to close. */
export function Lightbox({ items, index, onIndex, onClose }: {
  items: LightboxItem[]
  index: number | null
  onIndex: (index: number) => void
  onClose: () => void
}) {
  const touchX = useRef<number | null>(null)
  const count = items.length
  const open = index != null && index >= 0 && index < count

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      else if (e.key === 'ArrowRight' && count > 1) onIndex((index + 1) % count)
      else if (e.key === 'ArrowLeft' && count > 1) onIndex((index - 1 + count) % count)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, index, count, onIndex, onClose])

  if (!open) return null
  const item = items[index]
  const go = (delta: number) => onIndex((index + delta + count) % count)

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex flex-col bg-slate-950/95 text-white no-print"
      role="dialog"
      aria-modal="true"
      aria-label={item.title ?? 'Image viewer'}
      onTouchStart={(e) => (touchX.current = e.touches[0].clientX)}
      onTouchEnd={(e) => {
        if (touchX.current == null || count < 2) return
        const dx = e.changedTouches[0].clientX - touchX.current
        touchX.current = null
        if (Math.abs(dx) > 50) go(dx < 0 ? 1 : -1)
      }}
    >
      <div className="flex items-center gap-3 px-4 py-3">
        <div className="min-w-0 flex-1">
          {item.title && <div className="truncate font-medium">{item.title}</div>}
          {count > 1 && <div className="text-xs text-white/60">{index + 1} / {count}</div>}
        </div>
        <button className="grid h-9 w-9 place-items-center rounded-full hover:bg-white/10" onClick={onClose} aria-label="Close">
          <X className="h-5 w-5" />
        </button>
      </div>

      <div className="relative flex min-h-0 flex-1 items-center justify-center px-2 sm:px-16" onClick={(e) => e.target === e.currentTarget && onClose()}>
        <img key={item.src} src={item.src} alt={item.title ?? ''} className="max-h-full max-w-full select-none rounded object-contain shadow-2xl" />
        {count > 1 && (
          <>
            <button
              className="absolute left-2 top-1/2 grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full bg-white/10 hover:bg-white/20 sm:left-4"
              onClick={() => go(-1)}
              aria-label="Previous image"
            >
              <ChevronLeft className="h-6 w-6" />
            </button>
            <button
              className="absolute right-2 top-1/2 grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full bg-white/10 hover:bg-white/20 sm:right-4"
              onClick={() => go(1)}
              aria-label="Next image"
            >
              <ChevronRight className="h-6 w-6" />
            </button>
          </>
        )}
      </div>

      <div className="min-h-[3rem] px-4 py-3 text-center text-sm text-white/80">{item.caption}</div>
    </div>,
    document.body,
  )
}
