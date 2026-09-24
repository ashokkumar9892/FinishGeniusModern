import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Search, Wand2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { Card, EmptyState, Field, Note } from '@/components/ui'
import { useToast } from '@/components/toast'
import { PhotoReader } from './PhotoReader'
import { NoSamples } from './ColorMatchingPage'
import { gradeClass, methodLabel, sourceLabel, type ColorMatchResult, type ColorSampleRow } from './types'

/**
 * "Here is the colour I want — what do I mix?" The answer is ranked by ΔE00 against the samples that have actually
 * been measured, and every line says what it is standing on.
 */
export function MatchTab({ groupId, samples, onNewSample }: {
  groupId: number
  samples: ColorSampleRow[]
  onNewSample: () => void
}) {
  const toast = useToast()
  const [target, setTarget] = useState({ l: '', a: '', b: '' })
  const [species, setSpecies] = useState('')
  const [topcoat, setTopcoat] = useState('')
  const [wood, setWood] = useState({ l: '', a: '', b: '' })
  const [result, setResult] = useState<ColorMatchResult | null>(null)

  const speciesList = [...new Set(samples.map((s) => s.woodSpecies))].sort()
  const topcoats = [...new Set(samples.map((s) => s.topcoat).filter(Boolean) as string[])].sort()

  const match = useMutation({
    mutationFn: async () =>
      (await api.post<ColorMatchResult>('/color-matching/match', {
        groupId,
        l: Number(target.l), a: Number(target.a), b: Number(target.b),
        woodSpecies: species || null,
        topcoat: topcoat || null,
        woodL: wood.l === '' ? null : Number(wood.l),
        woodA: wood.a === '' ? null : Number(wood.a),
        woodB: wood.b === '' ? null : Number(wood.b),
      })).data,
    onSuccess: setResult,
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (samples.length === 0) return <NoSamples onNewSample={onNewSample} />
  const ready = target.l !== '' && target.a !== '' && target.b !== ''

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,420px)_minmax(0,1fr)]">
      <Card title="The colour you want">
        <div className="space-y-4">
          <Field label="Target L* a* b*" required hint="From the spectrophotometer, or read it off a photo below.">
            <div className="grid grid-cols-3 gap-2">
              <input className="input tabular-nums" placeholder="L*" value={target.l} onChange={(e) => setTarget({ ...target, l: e.target.value })} />
              <input className="input tabular-nums" placeholder="a*" value={target.a} onChange={(e) => setTarget({ ...target, a: e.target.value })} />
              <input className="input tabular-nums" placeholder="b*" value={target.b} onChange={(e) => setTarget({ ...target, b: e.target.value })} />
            </div>
          </Field>

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Wood" hint="Only samples on this wood.">
              <select className="input" value={species} onChange={(e) => setSpecies(e.target.value)}>
                <option value="">Any wood</option>
                {speciesList.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </Field>
            <Field label="Topcoat">
              <select className="input" value={topcoat} onChange={(e) => setTopcoat(e.target.value)}>
                <option value="">Any topcoat</option>
                {topcoats.map((t) => <option key={t} value={t}>{t}</option>)}
              </select>
            </Field>
          </div>

          <Field
            label="This board's unfinished colour (optional)"
            hint="Given this, a sample's measured shift is applied to your board instead of assuming the same wood."
          >
            <div className="grid grid-cols-3 gap-2">
              <input className="input tabular-nums" placeholder="L*" value={wood.l} onChange={(e) => setWood({ ...wood, l: e.target.value })} />
              <input className="input tabular-nums" placeholder="a*" value={wood.a} onChange={(e) => setWood({ ...wood, a: e.target.value })} />
              <input className="input tabular-nums" placeholder="b*" value={wood.b} onChange={(e) => setWood({ ...wood, b: e.target.value })} />
            </div>
          </Field>

          <button className="btn-primary w-full" disabled={!ready || match.isPending} onClick={() => match.mutate()}>
            <Search className="h-4 w-4" /> Find the closest formulas
          </button>

          <details className="rounded-md border">
            <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Read the target off a photo</summary>
            <div className="border-t p-3">
              <PhotoReader
                groupId={groupId}
                onRead={(r) => setTarget({ l: String(r.l), a: String(r.a), b: String(r.b) })}
              />
            </div>
          </details>
        </div>
      </Card>

      <div className="space-y-4">
        {!result && (
          <Card>
            <EmptyState
              icon={<Wand2 className="h-5 w-5" />}
              title="No search yet"
              description="Enter the colour you are trying to hit, or read it off a photograph, and the recorded formulas will be ranked by how close they land."
            />
          </Card>
        )}

        {result && (
          <>
            <Card
              title={
                <span className="inline-flex items-center gap-2">
                  Closest formulas
                  <span className="h-5 w-5 rounded border" style={{ background: result.target.hex }} title={result.target.hex} />
                  <span className="text-xs font-normal text-muted-foreground tabular-nums">
                    target L* {result.target.l} a* {result.target.a} b* {result.target.b}
                  </span>
                </span>
              }
              bodyClassName="p-0"
            >
              {result.matches.length === 0 ? (
                <EmptyState title="Nothing recorded that fits" description="No sample matches those filters. Try any wood, or record a sample of the formula you have in mind." />
              ) : (
                <ul className="divide-y">
                  {result.matches.map((m) => (
                    <li key={m.sampleId} className="flex flex-wrap items-center gap-3 p-3">
                      <span className="flex shrink-0 items-center gap-1" title="Predicted colour next to your target">
                        <span className="h-10 w-10 rounded border" style={{ background: m.predicted.hex }} />
                        <span className="h-10 w-10 rounded border" style={{ background: result.target.hex }} />
                      </span>
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-medium">{m.formulaName}</span>
                          <span className={clsx('badge', gradeClass(m.deltaE))}>ΔE {m.deltaE} · {m.grade}</span>
                          <span className="badge bg-muted text-muted-foreground">{m.confidence}% confidence</span>
                        </div>
                        <div className="mt-0.5 text-xs text-muted-foreground">
                          {m.woodSpecies}
                          {m.suggestedConcentration != null && (
                            <>
                              {' · '}
                              <span className="font-medium text-foreground">mix at {m.suggestedConcentration}%</span>
                              {m.recordedConcentration != null && m.recordedConcentration !== m.suggestedConcentration && (
                                <> (nearest sample {m.recordedConcentration}%)</>
                              )}
                            </>
                          )}
                          {m.method != null && ` · ${methodLabel(m.method)}`}
                          {m.coats != null && ` · ${m.coats} coat${m.coats === 1 ? '' : 's'}`}
                          {m.topcoat && ` · ${m.topcoat}`}
                          {` · ${sourceLabel(m.source)}`}
                        </div>
                        <div className="mt-0.5 text-xs text-muted-foreground">{m.basis}</div>
                        {m.colorants && m.colorants.colorants.length > 0 && (
                          <ul className="mt-1.5 flex flex-wrap gap-x-3 gap-y-0.5 text-xs">
                            {m.colorants.colorants.map((c) => (
                              <li key={c.materialId} className="tabular-nums">
                                <span className="text-muted-foreground">{c.productName}</span>{' '}
                                <span className="font-medium">{c.percent}%</span>
                              </li>
                            ))}
                          </ul>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Card>

            <Note tone="info">
              Ranked against {result.sampleCount} recorded sample{result.sampleCount === 1 ? '' : 's'}. A recommendation is
              a starting point to mix and measure, not a finished match: record what you actually get, and the next search
              on this colour will be better for it.
            </Note>
          </>
        )}
      </div>
    </div>
  )
}
