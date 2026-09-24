import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Camera, Pipette, Wand2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { Field, Note } from '@/components/ui'
import { useToast } from '@/components/toast'
import { RegionPicker } from './RegionPicker'
import type { PhotoReading, Region } from './types'

/**
 * Reads a colour off a photograph: upload, drag a box over the wood, and — if a grey or white card is in the shot —
 * a second box over the card. Without the card the number still comes back, but marked uncalibrated, because the
 * room's light is baked into it.
 */
export function PhotoReader({ groupId, onRead, storedFile, onPhoto }: {
  groupId: number
  onRead: (reading: PhotoReading) => void
  /** The photo to work on, when the parent already has one. */
  storedFile?: string | null
  onPhoto?: (storedFile: string | null) => void
}) {
  const toast = useToast()
  const [file, setFile] = useState<string | null>(storedFile ?? null)
  const [wood, setWood] = useState<Region | null>(null)
  const [card, setCard] = useState<Region | null>(null)
  const [chart, setChart] = useState<Region | null>(null)
  const [active, setActive] = useState<'wood' | 'card' | 'chart'>('wood')
  const [cardType, setCardType] = useState<'grey' | 'white'>('grey')
  const [reading, setReading] = useState<PhotoReading | null>(null)
  const [hint, setHint] = useState<string | null>(null)

  const upload = useMutation({
    mutationFn: async (f: File) => {
      const form = new FormData()
      form.append('groupId', String(groupId))
      form.append('file', f)
      return (await api.post<{ storedFile: string }>('/color-matching/photo', form)).data
    },
    onSuccess: (d) => {
      setFile(d.storedFile)
      setWood(null)
      setCard(null)
      setChart(null)
      setReading(null)
      onPhoto?.(d.storedFile)
      suggest.mutate(d.storedFile) // offer the boxes straight away; they can still be dragged
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const suggest = useMutation({
    mutationFn: async (storedFile: string) =>
      (await api.post<{ sample: Region | null; chart: Region | null; note: string }>('/color-matching/suggest-regions', {
        groupId, storedFile,
      })).data,
    onSuccess: (d) => {
      if (d.sample) setWood(d.sample)
      if (d.chart) {
        setChart(d.chart)
        setActive('wood')
      }
      setHint(d.note)
    },
    onError: () => setHint(null), // finding the areas is a convenience; dragging them still works
  })

  const read = useMutation({
    mutationFn: async () =>
      (await api.post<PhotoReading>('/color-matching/read-photo', {
        groupId, storedFile: file, sample: wood, card, cardType, chart,
      })).data,
    onSuccess: (d) => {
      setReading(d)
      onRead(d)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <div className="space-y-3">
      <Field label="Photograph" hint="A fixed camera, even lighting and a ColorChecker (or a grey card) in the shot give a reading worth keeping.">
        <label className="btn-secondary cursor-pointer">
          <Camera className="h-4 w-4" /> {file ? 'Choose another photo' : 'Choose a photo'}
          <input
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) upload.mutate(f)
              e.target.value = ''
            }}
          />
        </label>
      </Field>

      {file && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <div className="inline-flex rounded-md border bg-muted/40 p-0.5" role="group" aria-label="What to mark next">
              {([['wood', 'Mark the wood'], ['chart', 'Mark the ColorChecker'], ['card', 'Mark a grey card']] as const).map(([k, label]) => (
                <button
                  key={k}
                  type="button"
                  aria-pressed={active === k}
                  className={`h-7 rounded px-2.5 text-xs font-medium ${active === k ? 'bg-card shadow-sm' : 'text-muted-foreground'}`}
                  onClick={() => setActive(k)}
                >
                  {label}
                </button>
              ))}
            </div>
            <button type="button" className="btn-secondary btn-sm" disabled={suggest.isPending} onClick={() => file && suggest.mutate(file)}>
              <Wand2 className="h-4 w-4" /> Find the areas
            </button>
            {active === 'card' && (
              <select className="input h-8 w-32 text-sm" value={cardType} onChange={(e) => setCardType(e.target.value as 'grey' | 'white')} aria-label="Card type">
                <option value="grey">Grey card</option>
                <option value="white">White card</option>
              </select>
            )}
            <button
              type="button"
              className="btn-primary btn-sm"
              disabled={!wood || read.isPending}
              onClick={() => read.mutate()}
              title={wood ? undefined : 'Drag a box over the wood first'}
            >
              <Pipette className="h-4 w-4" /> Read the colour
            </button>
          </div>

          <RegionPicker
            src={fileUrl(file, false, null, 1200)}
            active={active}
            onChange={(k, r) => (k === 'wood' ? setWood(r) : k === 'chart' ? setChart(r) : setCard(r))}
            regions={[
              { key: 'wood', region: wood, label: 'Wood', color: '#f97316' },
              { key: 'chart', region: chart, label: 'ColorChecker', color: '#22c55e' },
              { key: 'card', region: card, label: cardType === 'white' ? 'White card' : 'Grey card', color: '#38bdf8' },
            ]}
            className="max-h-[420px]"
          />

          {hint && <div className="text-xs text-muted-foreground">{hint}</div>}

          {reading && (
            <div className="flex flex-wrap items-center gap-3 rounded-md border p-3">
              <span className="h-10 w-10 shrink-0 rounded border" style={{ background: reading.hex }} />
              <div className="text-sm">
                <div className="font-medium tabular-nums">
                  L* {reading.l} a* {reading.a} b* {reading.b}
                </div>
                <div className="text-xs text-muted-foreground">{reading.note}</div>
              </div>
            </div>
          )}
          {reading && !reading.calibrated && (
            <Note>
              This reading has no calibration card behind it, so it carries the room's lighting. Keep it for comparing and
              previewing — record a spectrophotometer reading when the number has to be right.
            </Note>
          )}
        </>
      )}
    </div>
  )
}
