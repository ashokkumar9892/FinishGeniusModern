import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Save } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { ErrorBanner, Field, Modal, Note } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { useToast } from '@/components/toast'
import { PhotoReader } from './PhotoReader'
import { METHODS, SOURCES, type ColorSampleRow } from './types'

interface FormState {
  name: string
  woodSpecies: string
  sandingGrit: string
  woodL: string; woodA: string; woodB: string
  formulaId: number | null
  formulaName: string
  concentration: string
  method: string
  coats: string
  wetFilmMils: string
  flashMinutes: string
  sealer: string
  topcoat: string
  sheen: string
  finalL: string; finalA: string; finalB: string
  source: string
  measuredAt: string
  notes: string
  photoFile: string | null
}

const empty: FormState = {
  name: '', woodSpecies: '', sandingGrit: '', woodL: '', woodA: '', woodB: '', formulaId: null, formulaName: '',
  concentration: '', method: '1', coats: '', wetFilmMils: '', flashMinutes: '', sealer: '', topcoat: '', sheen: '',
  finalL: '', finalA: '', finalB: '', source: '1', measuredAt: '', notes: '', photoFile: null,
}

const num = (v: string) => (v.trim() === '' ? null : Number(v))

/** A number typed into a box: empty is fine where the field is optional, letters never are. */
const badNumber = (v: string, { required = false, min, max }: { required?: boolean; min?: number; max?: number } = {}) => {
  if (v.trim() === '') return required ? 'Required.' : null
  const n = Number(v)
  if (!Number.isFinite(n)) return 'Numbers only.'
  if (min != null && n < min) return `Must be ${min} or more.`
  if (max != null && n > max) return `Must be ${max} or less.`
  return null
}

/** Records one finished sample: the wood, the stain, how it went on, and what it measured. */
export function SampleModal({ open, onClose, groupId, sample }: {
  open: boolean
  onClose: () => void
  groupId: number
  sample: ColorSampleRow | null
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState<FormState>(empty)
  const [error, setError] = useState<string | null>(null)
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }))

  useEffect(() => {
    if (!open) return
    setError(null)
    setForm(
      sample
        ? {
            name: sample.name, woodSpecies: sample.woodSpecies, sandingGrit: String(sample.sandingGrit ?? ''),
            woodL: String(sample.wood?.l ?? ''), woodA: String(sample.wood?.a ?? ''), woodB: String(sample.wood?.b ?? ''),
            formulaId: sample.formulaId ?? null, formulaName: sample.formulaName,
            concentration: String(sample.concentration ?? ''), method: String(sample.method ?? ''),
            coats: String(sample.coats ?? ''), wetFilmMils: String(sample.wetFilmMils ?? ''),
            flashMinutes: String(sample.flashMinutes ?? ''), sealer: sample.sealer ?? '', topcoat: sample.topcoat ?? '',
            sheen: String(sample.sheen ?? ''), finalL: String(sample.final.l), finalA: String(sample.final.a),
            finalB: String(sample.final.b), source: String(sample.source),
            measuredAt: sample.measuredAt ? sample.measuredAt.slice(0, 10) : '', notes: sample.notes ?? '',
            photoFile: sample.photoFile ?? null,
          }
        : empty,
    )
  }, [open, sample])

  const formulas = useQuery({
    queryKey: ['formulas', groupId],
    queryFn: () => api.get<{ id: number; name: string; number?: string | null }[]>('/formulas', { params: { groupId } }).then((r) => r.data),
    enabled: open && groupId > 0,
  })

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        groupId,
        name: form.name.trim() || null,
        woodSpecies: form.woodSpecies.trim(),
        sandingGrit: num(form.sandingGrit),
        woodL: num(form.woodL), woodA: num(form.woodA), woodB: num(form.woodB),
        formulaId: form.formulaId,
        formulaName: form.formulaName.trim(),
        concentration: num(form.concentration),
        method: num(form.method),
        coats: num(form.coats),
        wetFilmMils: num(form.wetFilmMils),
        flashMinutes: num(form.flashMinutes),
        sealer: form.sealer.trim() || null,
        topcoat: form.topcoat.trim() || null,
        sheen: num(form.sheen),
        finalL: Number(form.finalL), finalA: Number(form.finalA), finalB: Number(form.finalB),
        source: Number(form.source),
        measuredAt: form.measuredAt || null,
        notes: form.notes.trim() || null,
        photoFile: form.photoFile,
      }
      return sample
        ? (await api.put<{ message: string }>(`/color-matching/samples/${sample.id}`, body)).data
        : (await api.post<{ message: string }>('/color-matching/samples', body)).data
    },
    onSuccess: (r) => {
      toast.success(r.message)
      qc.invalidateQueries({ queryKey: ['color-samples'] })
      qc.invalidateQueries({ queryKey: ['color-species'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  // Every box that holds a number is checked here, so nothing typed by mistake reaches the database as a blank.
  const errors: Record<string, string | null> = {
    sandingGrit: badNumber(form.sandingGrit, { min: 0, max: 2000 }),
    woodL: badNumber(form.woodL, { min: 0, max: 100 }),
    woodA: badNumber(form.woodA, { min: -128, max: 128 }),
    woodB: badNumber(form.woodB, { min: -128, max: 128 }),
    concentration: badNumber(form.concentration, { min: 0, max: 100 }),
    coats: badNumber(form.coats, { min: 0, max: 20 }),
    wetFilmMils: badNumber(form.wetFilmMils, { min: 0 }),
    flashMinutes: badNumber(form.flashMinutes, { min: 0 }),
    sheen: badNumber(form.sheen, { min: 0, max: 100 }),
    finalL: badNumber(form.finalL, { required: true, min: 0, max: 100 }),
    finalA: badNumber(form.finalA, { required: true, min: -128, max: 128 }),
    finalB: badNumber(form.finalB, { required: true, min: -128, max: 128 }),
  }
  const hasErrors = Object.values(errors).some(Boolean)

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="xl"
      title={sample ? `Edit sample: ${sample.name}` : 'New colour sample'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>Cancel</button>
          <button
            className="btn-primary"
            disabled={save.isPending || hasErrors || !form.woodSpecies.trim() || !(form.formulaId || form.formulaName.trim())}
            onClick={() => save.mutate()}
          >
            <Save className="h-4 w-4" /> Save
          </button>
        </>
      }
    >
      <div className="space-y-5">
        <ErrorBanner message={error} />

        <section className="space-y-3">
          <h4 className="text-sm font-semibold">The wood</h4>
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="Species" required hint="Maple, Oak, Cherry…">
              <input className="input" value={form.woodSpecies} onChange={(e) => set('woodSpecies', e.target.value)} maxLength={100} />
            </Field>
            <Field label="Sanded to (grit)" error={errors.sandingGrit ?? undefined}>
              <input className={clsx('input', errors.sandingGrit && 'input-invalid')} inputMode="numeric" value={form.sandingGrit} onChange={(e) => set('sandingGrit', e.target.value)} />
            </Field>
            <Field label="Measured on">
              <input className="input" type="date" value={form.measuredAt} onChange={(e) => set('measuredAt', e.target.value)} />
            </Field>
          </div>
          <Field
            label="Unfinished wood colour (L* a* b*)"
            hint="Optional, and worth doing: it lets the same formula be predicted on a different board."
            error={[errors.woodL, errors.woodA, errors.woodB].find(Boolean) ?? undefined}
          >
            <div className="grid grid-cols-3 gap-2">
              <input className={clsx('input tabular-nums', errors.woodL && 'input-invalid')} placeholder="L*" value={form.woodL} onChange={(e) => set('woodL', e.target.value)} />
              <input className={clsx('input tabular-nums', errors.woodA && 'input-invalid')} placeholder="a*" value={form.woodA} onChange={(e) => set('woodA', e.target.value)} />
              <input className={clsx('input tabular-nums', errors.woodB && 'input-invalid')} placeholder="b*" value={form.woodB} onChange={(e) => set('woodB', e.target.value)} />
            </div>
          </Field>
        </section>

        <section className="space-y-3">
          <h4 className="text-sm font-semibold">The stain</h4>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Formula" required hint="Pick one of this group's formulas, or type a name below.">
              <SearchSelect
                options={(formulas.data ?? []).map((f) => ({ value: f.id, label: f.name, sub: f.number ?? undefined }))}
                value={form.formulaId}
                onChange={(v) => {
                  const id = v as number | null
                  set('formulaId', id)
                  const picked = (formulas.data ?? []).find((f) => f.id === id)
                  if (picked) set('formulaName', picked.name)
                }}
                placeholder={formulas.isLoading ? 'Loading formulas…' : 'Select a formula'}
              />
            </Field>
            <Field label="Formula name" required>
              <input className="input" value={form.formulaName} onChange={(e) => set('formulaName', e.target.value)} maxLength={400} />
            </Field>
          </div>
          <div className="grid gap-3 sm:grid-cols-4">
            <Field label="Strength (%)" error={errors.concentration ?? undefined}>
              <input className={clsx('input tabular-nums', errors.concentration && 'input-invalid')} inputMode="decimal" value={form.concentration} onChange={(e) => set('concentration', e.target.value)} />
            </Field>
            <Field label="Applied by">
              <select className="input" value={form.method} onChange={(e) => set('method', e.target.value)}>
                <option value="">—</option>
                {METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
              </select>
            </Field>
            <Field label="Coats" error={errors.coats ?? undefined}>
              <input className={clsx('input tabular-nums', errors.coats && 'input-invalid')} inputMode="numeric" value={form.coats} onChange={(e) => set('coats', e.target.value)} />
            </Field>
            <Field label="Wet film (mils)" error={errors.wetFilmMils ?? undefined}>
              <input className={clsx('input tabular-nums', errors.wetFilmMils && 'input-invalid')} inputMode="decimal" value={form.wetFilmMils} onChange={(e) => set('wetFilmMils', e.target.value)} />
            </Field>
          </div>
          <div className="grid gap-3 sm:grid-cols-4">
            <Field label="Flash (minutes)" error={errors.flashMinutes ?? undefined}>
              <input className={clsx('input tabular-nums', errors.flashMinutes && 'input-invalid')} inputMode="numeric" value={form.flashMinutes} onChange={(e) => set('flashMinutes', e.target.value)} />
            </Field>
            <Field label="Sealer">
              <input className="input" value={form.sealer} onChange={(e) => set('sealer', e.target.value)} maxLength={200} />
            </Field>
            <Field label="Topcoat">
              <input className="input" value={form.topcoat} onChange={(e) => set('topcoat', e.target.value)} maxLength={200} />
            </Field>
            <Field label="Sheen (%)" error={errors.sheen ?? undefined}>
              <input className={clsx('input tabular-nums', errors.sheen && 'input-invalid')} inputMode="decimal" value={form.sheen} onChange={(e) => set('sheen', e.target.value)} />
            </Field>
          </div>
        </section>

        <section className="space-y-3">
          <h4 className="text-sm font-semibold">The finished colour</h4>
          <div className="grid gap-3 sm:grid-cols-[1fr_auto]">
            <Field
              label="Measured L* a* b*"
              required
              hint="L* 0-100 (dark to light), a* green-red, b* blue-yellow."
              error={[errors.finalL, errors.finalA, errors.finalB].find(Boolean) ?? undefined}
            >
              <div className="grid grid-cols-3 gap-2">
                <input className={clsx('input tabular-nums', errors.finalL && 'input-invalid')} placeholder="L*" value={form.finalL} onChange={(e) => set('finalL', e.target.value)} />
                <input className={clsx('input tabular-nums', errors.finalA && 'input-invalid')} placeholder="a*" value={form.finalA} onChange={(e) => set('finalA', e.target.value)} />
                <input className={clsx('input tabular-nums', errors.finalB && 'input-invalid')} placeholder="b*" value={form.finalB} onChange={(e) => set('finalB', e.target.value)} />
              </div>
            </Field>
            <Field label="Read from" required>
              <select className="input sm:w-56" value={form.source} onChange={(e) => set('source', e.target.value)}>
                {SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </Field>
          </div>

          <details className="rounded-md border">
            <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Read it from a photo instead</summary>
            <div className="border-t p-3">
              <PhotoReader
                groupId={groupId}
                storedFile={form.photoFile}
                onPhoto={(f) => set('photoFile', f)}
                onRead={(r) => {
                  set('finalL', String(r.l))
                  set('finalA', String(r.a))
                  set('finalB', String(r.b))
                  set('source', '2')
                }}
              />
            </div>
          </details>
          {form.source === '2' && (
            <Note tone="info">
              This sample is filed as a photo reading. Predictions lean on spectrophotometer readings first, and say so.
            </Note>
          )}
          <Field label="Notes">
            <textarea className="input" rows={2} value={form.notes} onChange={(e) => set('notes', e.target.value)} maxLength={4000} />
          </Field>
        </section>
      </div>
    </Modal>
  )
}
