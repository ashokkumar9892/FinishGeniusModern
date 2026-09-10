import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts'
import { num } from '@/lib/format'

/*
 * Categorical slots in fixed order (validated reference palette; light / dark steps). Colour follows the ingredient's
 * position in the formula, never its size, so editing grams never repaints other slices. Past 8 ingredients the tail
 * folds into a neutral "Other" slice instead of generating new hues.
 */
const DONUT_CSS = `
.fg-donut{--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--s4:#eda100;--s5:#e87ba4;--s6:#008300;--s7:#4a3aa7;--s8:#e34948;--so:#898781}
[data-theme='dark'] .fg-donut{--s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;--s5:#d55181;--s6:#008300;--s7:#9085e9;--s8:#e66767}
`
const SLOTS = 8

export interface DonutItem {
  key: string
  name: string
  grams: number
}

interface Slice {
  key: string
  name: string
  grams: number
  color: string
  percent: number
}

function toSlices(items: DonutItem[]): Slice[] {
  const valid = items.filter((i) => i.grams > 0)
  const total = valid.reduce((s, i) => s + i.grams, 0)
  const pct = (g: number) => (total > 0 ? (g / total) * 100 : 0)
  // Colours are assigned by ingredient order over *all* items so a slice keeps its colour when others hit 0 g.
  const colorOf = new Map(items.map((i, idx) => [i.key, `var(--s${idx + 1})`]))
  if (items.length <= SLOTS) return valid.map((i) => ({ ...i, color: colorOf.get(i.key)!, percent: pct(i.grams) }))
  const head = valid.filter((i) => items.indexOf(i) < SLOTS - 1)
  const tail = valid.filter((i) => items.indexOf(i) >= SLOTS - 1)
  const slices = head.map((i) => ({ ...i, color: colorOf.get(i.key)!, percent: pct(i.grams) }))
  if (tail.length) {
    const grams = tail.reduce((s, i) => s + i.grams, 0)
    slices.push({ key: 'other', name: `Other (${tail.length})`, grams, color: 'var(--so)', percent: pct(grams) })
  }
  return slices
}

function DonutTooltip({ active, payload }: { active?: boolean; payload?: { payload?: Slice }[] }) {
  const s = payload?.[0]?.payload
  if (!active || !s) return null
  return (
    <div className="card px-3 py-2 text-xs shadow-lg">
      <div className="mb-0.5 flex items-center gap-1.5 font-medium">
        <span className="h-2.5 w-2.5 rounded-full" style={{ background: s.color }} />
        {s.name}
      </div>
      <div className="text-muted-foreground tabular-nums">
        {num(s.grams, 2)} g · {num(s.percent, 1)}%
      </div>
    </div>
  )
}

/** Composition of the batch by weight (grams), with the total in the hole and a legend underneath. */
export function CompositionDonut({ items }: { items: DonutItem[] }) {
  const slices = toSlices(items)
  const total = slices.reduce((s, i) => s + i.grams, 0)
  if (slices.length === 0)
    return <div className="flex h-40 items-center justify-center rounded-md border border-dashed text-xs text-muted-foreground">Add ingredients with grams to see the composition.</div>

  return (
    <div className="fg-donut">
      <style>{DONUT_CSS}</style>
      <div className="relative h-52">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie data={slices} dataKey="grams" nameKey="name" innerRadius="62%" outerRadius="92%" startAngle={90} endAngle={-270} isAnimationActive={false}>
              {slices.map((s) => (
                <Cell key={s.key} style={{ fill: s.color, stroke: 'hsl(var(--card))', strokeWidth: 2, outline: 'none' }} />
              ))}
            </Pie>
            <Tooltip content={<DonutTooltip />} />
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <div className="text-lg font-semibold">{num(total, 1)} g</div>
          <div className="text-[11px] text-muted-foreground">total weight</div>
        </div>
      </div>
      <ul className="mt-3 space-y-1 text-xs">
        {slices.map((s) => (
          <li key={s.key} className="flex items-center gap-2">
            <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: s.color }} />
            <span className="min-w-0 flex-1 truncate" title={s.name}>
              {s.name}
            </span>
            <span className="tabular-nums text-muted-foreground">{num(s.percent, 1)}%</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
