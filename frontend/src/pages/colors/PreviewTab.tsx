import { useEffect, useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Camera, Image as ImageIcon, Wand2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { Card, EmptyState, Field, Note } from '@/components/ui'
import { useToast } from '@/components/toast'
import { RegionPicker } from './RegionPicker'
import { NoSamples } from './ColorMatchingPage'
import type { ColorSampleRow, Region } from './types'

interface Prediction {
  predicted?: { l: number; a: number; b: number; hex: string }
  confidence: number
  basis: string
  samplesUsed: number
}

/**
 * The customer's own unfinished wood, shown as it should look with a chosen formula on it. The colour comes from the
 * measured samples; the picture only carries it onto this board's grain.
 */
export function PreviewTab({ groupId, samples }: { groupId: number; samples: ColorSampleRow[] }) {
  const toast = useToast()
  const [file, setFile] = useState<string | null>(null)
  const [wood, setWood] = useState<Region | null>(null)
  const [formulaName, setFormulaName] = useState('')
  const [species, setSpecies] = useState('')
  const [strength, setStrength] = useState('')
  const [prediction, setPrediction] = useState<Prediction | null>(null)
  const [preview, setPreview] = useState<string | null>(null)

  const formulas = useMemo(() => [...new Set(samples.map((s) => s.formulaName))].sort(), [samples])
  const speciesList = useMemo(() => [...new Set(samples.map((s) => s.woodSpecies))].sort(), [samples])
  const strengths = useMemo(
    () => [...new Set(samples.filter((s) => s.formulaName === formulaName && s.concentration != null).map((s) => s.concentration!))].sort((a, b) => a - b),
    [samples, formulaName],
  )

  // Nothing shown may outlive the choice it was made for.
  useEffect(() => {
    setPreview(null)
    setPrediction(null)
  }, [formulaName, strength, species, file])

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
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const render = useMutation({
    mutationFn: async () => {
      const p = (await api.post<Prediction>('/color-matching/predict', {
        groupId,
        formulaName,
        concentration: strength === '' ? null : Number(strength),
        woodSpecies: species || null,
      })).data
      if (!p.predicted) return { prediction: p, image: null as string | null }
      const image = await api.post('/color-matching/preview', {
        groupId, storedFile: file, wood, l: p.predicted.l, a: p.predicted.a, b: p.predicted.b,
      }, { responseType: 'blob' })
      return { prediction: p, image: URL.createObjectURL(image.data as Blob) }
    },
    onSuccess: ({ prediction, image }) => {
      setPrediction(prediction)
      setPreview((old) => {
        if (old) URL.revokeObjectURL(old)
        return image
      })
      if (!image) toast.error('There is no sample for that formula yet, so there is nothing to show.')
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (samples.length === 0) return <NoSamples />

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,420px)_minmax(0,1fr)]">
      <Card title="The board and the stain">
        <div className="space-y-4">
          <Field label="Photo of the unfinished wood" required>
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
            <Field label="Drag a box over the bare wood" required hint="Away from shadows, edges and anything that is not the board.">
              <RegionPicker
                src={fileUrl(file, false, null, 1200)}
                active="wood"
                onChange={(_, r) => setWood(r)}
                regions={[{ key: 'wood', region: wood, label: 'Wood', color: '#f97316' }]}
                className="max-h-[320px]"
              />
            </Field>
          )}

          <Field label="Formula" required>
            <select className="input" value={formulaName} onChange={(e) => setFormulaName(e.target.value)}>
              <option value="">Select a formula</option>
              {formulas.map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Strength (%)" hint={strengths.length ? `Measured: ${strengths.join(', ')}` : 'No strengths recorded'}>
              <input className="input tabular-nums" inputMode="decimal" value={strength} onChange={(e) => setStrength(e.target.value)} />
            </Field>
            <Field label="Wood">
              <select className="input" value={species} onChange={(e) => setSpecies(e.target.value)}>
                <option value="">Any recorded</option>
                {speciesList.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </Field>
          </div>

          <button className="btn-primary w-full" disabled={!file || !wood || !formulaName || render.isPending} onClick={() => render.mutate()}>
            <Wand2 className="h-4 w-4" /> Show it stained
          </button>
        </div>
      </Card>

      <Card title="Predicted result" bodyClassName={preview ? 'p-0' : undefined}>
        {!preview && (
          <EmptyState
            icon={<ImageIcon className="h-5 w-5" />}
            title="Nothing rendered yet"
            description="Pick a photo, mark the bare wood and choose a formula. The colour comes from the samples recorded for it."
          />
        )}
        {preview && (
          <>
            <img src={preview} alt="The wood with the predicted stain colour" className="block w-full" />
            {prediction?.predicted && (
              <div className="flex flex-wrap items-center gap-3 border-t p-3">
                <span className="h-10 w-10 rounded border" style={{ background: prediction.predicted.hex }} />
                <div className="text-sm">
                  <div className="font-medium tabular-nums">
                    Predicted L* {prediction.predicted.l} a* {prediction.predicted.a} b* {prediction.predicted.b}
                  </div>
                  <div className="text-xs text-muted-foreground">
                    {prediction.confidence}% confidence · {prediction.basis} From {prediction.samplesUsed} sample
                    {prediction.samplesUsed === 1 ? '' : 's'}.
                  </div>
                </div>
              </div>
            )}
          </>
        )}
        {preview && (
          <div className="border-t p-3">
            <Note>
              The colour is predicted from measured samples; the grain, gloss and any blotching are this photograph's, not
              a prediction. Treat it as a guide for the customer, and prove it on a real sample board before finishing.
            </Note>
          </div>
        )}
      </Card>
    </div>
  )
}
