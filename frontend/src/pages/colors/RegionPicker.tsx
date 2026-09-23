import { useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import clsx from 'clsx'
import type { Region } from './types'

/**
 * Drag a box on a photograph. Two are used together on the colour screens: one over the wood, one over the grey or
 * white calibration card. Boxes are kept as fractions of the picture, so they mean the same thing whatever size the
 * photo is shown at, and the server reads the same area out of the full-resolution original.
 */
export function RegionPicker({ src, regions, active, onChange, className }: {
  src: string
  /** The boxes to draw, in order; each with its own colour and label. */
  regions: { key: string; region: Region | null; label: string; color: string }[]
  /** Which box the next drag sets. */
  active: string
  onChange: (key: string, region: Region) => void
  className?: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [drag, setDrag] = useState<{ x: number; y: number; region: Region } | null>(null)

  const point = (e: ReactPointerEvent) => {
    const box = ref.current!.getBoundingClientRect()
    return {
      x: Math.min(1, Math.max(0, (e.clientX - box.left) / box.width)),
      y: Math.min(1, Math.max(0, (e.clientY - box.top) / box.height)),
    }
  }

  const start = (e: ReactPointerEvent) => {
    if (e.button !== 0) return
    const p = point(e)
    ;(e.target as Element).setPointerCapture?.(e.pointerId)
    setDrag({ x: p.x, y: p.y, region: { x: p.x, y: p.y, width: 0, height: 0 } })
  }

  const move = (e: ReactPointerEvent) => {
    if (!drag) return
    const p = point(e)
    setDrag({
      ...drag,
      region: {
        x: Math.min(drag.x, p.x),
        y: Math.min(drag.y, p.y),
        width: Math.abs(p.x - drag.x),
        height: Math.abs(p.y - drag.y),
      },
    })
  }

  const end = () => {
    if (!drag) return
    // A tap with no drag would measure nothing; ignore anything under 2% of the picture.
    if (drag.region.width > 0.02 && drag.region.height > 0.02) onChange(active, drag.region)
    setDrag(null)
  }

  const shown = regions.map((r) => (r.key === active && drag ? { ...r, region: drag.region } : r))

  return (
    <div
      ref={ref}
      className={clsx('relative select-none overflow-hidden rounded-md border bg-muted touch-none', className)}
      onPointerDown={start}
      onPointerMove={move}
      onPointerUp={end}
      onPointerCancel={end}
    >
      <img src={src} alt="" className="pointer-events-none block w-full" draggable={false} />
      {shown.map((r) =>
        r.region ? (
          <div
            key={r.key}
            className="pointer-events-none absolute border-2"
            style={{
              left: `${r.region.x * 100}%`,
              top: `${r.region.y * 100}%`,
              width: `${r.region.width * 100}%`,
              height: `${r.region.height * 100}%`,
              borderColor: r.color,
              boxShadow: '0 0 0 9999px rgba(0,0,0,0)',
            }}
          >
            <span className="absolute left-0 top-0 -translate-y-full px-1 text-[11px] font-semibold" style={{ color: r.color }}>
              {r.label}
            </span>
          </div>
        ) : null,
      )}
    </div>
  )
}
