import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Check, ChevronLeft, ChevronRight, ExternalLink, Eye, FileText, Info, Layers, Save } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useLookups } from '@/lib/auth'
import { materialTypeLabel } from '@/lib/types'
import { useToast } from '@/components/toast'
import { SearchSelect } from '@/components/SearchSelect'
import { EmptyState, ErrorBanner, Field, LoadingBlock, PageHeader, Spinner } from '@/components/ui'
import { EntityDocuments } from '@/components/EntityDocuments'
import { confirmLeave, useMaterials, useUnsavedGuard, ViewStepModal } from './shared'
import type { BuilderCategory, BuilderEntry, BuilderPayload, BuilderPullDown, BuilderSubStep, CharacteristicDef } from './types'

interface ValueDraft {
  value: string
  materialId: number | null
}
interface PullDraft {
  categoryId: number | null
  values: Record<number, ValueDraft>
}
type Draft = Record<number, PullDraft>

interface Tile {
  key: string
  sub: BuilderSubStep
  pass: number
  label: string
}

const tileLabel = (sub: BuilderSubStep, pass: number) =>
  sub.passThroughs > 1 ? `${sub.sequence}${String.fromCharCode(64 + Math.min(pass, 26))}` : String(sub.sequence)

function draftFor(tile: Tile, entries: BuilderEntry[]): Draft {
  const d: Draft = {}
  for (const pd of tile.sub.pullDowns) {
    const e = entries.find((x) => x.subStepId === tile.sub.id && x.pass === tile.pass && x.pullDownId === pd.id)
    const values: Record<number, ValueDraft> = {}
    e?.values.forEach((v) => (values[v.characteristicId] = { value: v.value ?? '', materialId: v.materialId ?? null }))
    d[pd.id] = { categoryId: e?.categoryId ?? null, values }
  }
  return d
}

/** Canonical form used for dirty checks: only chosen categories and non-empty values. */
function normalize(d: Draft, tile: Tile | undefined) {
  if (!tile) return ''
  return JSON.stringify(
    tile.sub.pullDowns.map((pd) => {
      const p = d[pd.id]
      if (!p?.categoryId) return [pd.id, null]
      const cat = pd.categories.find((c) => c.id === p.categoryId)
      const vals = (cat?.characteristics ?? [])
        .map((c) => {
          const v = p.values[c.id]
          if (c.inputType === 'Material') return v?.materialId ? [c.id, v.materialId] : null
          return v?.value?.trim() ? [c.id, v.value.trim()] : null
        })
        .filter(Boolean)
      return [pd.id, p.categoryId, vals]
    }),
  )
}

const isNumber = (s: string) => /^-?\d*\.?\d+$/.test(s.trim().replace(/,/g, ''))

export default function ProcessStepBuilderPage() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { groupId, group } = useGroup()
  const lookups = useLookups()
  const toast = useToast()
  const qc = useQueryClient()

  const stepId = id && id !== 'new' && Number(id) > 0 ? Number(id) : null
  const [sectorId, setSectorId] = useState<number>(() => Number(params.get('sector') ?? 0))
  const [name, setName] = useState('')
  const [nameError, setNameError] = useState<string | null>(null)
  const [index, setIndex] = useState(0)
  const [draft, setDraft] = useState<Draft>({})
  const [initial, setInitial] = useState<Draft>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [viewOpen, setViewOpen] = useState(false)
  const [docsOpen, setDocsOpen] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)

  const q = useQuery({
    queryKey: ['process-step-builder', stepId ? 0 : groupId, stepId, stepId ? 0 : sectorId],
    queryFn: () =>
      api
        .get<BuilderPayload>('/process-steps/builder', {
          params: { groupId, stepId: stepId ?? undefined, industrySectorId: stepId ? undefined : sectorId || undefined },
        })
        .then((r) => r.data),
    placeholderData: keepPreviousData,
  })
  const data = q.data

  // Sync header fields from the server.
  useEffect(() => {
    if (!data) return
    if (data.industrySectorId !== sectorId && (stepId || !sectorId)) setSectorId(data.industrySectorId)
    if (data.step) setName((n) => (n ? n : data.step!.name))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data])

  const tiles = useMemo<Tile[]>(
    () =>
      (data?.subSteps ?? [])
        .filter((s) => s.userRole === 'User')
        .flatMap((s) => Array.from({ length: Math.max(1, s.passThroughs) }, (_, i) => ({ key: `${s.id}-${i + 1}`, sub: s, pass: i + 1, label: tileLabel(s, i + 1) }))),
    [data],
  )
  const tile = tiles[Math.min(index, Math.max(0, tiles.length - 1))]
  const filledKeys = useMemo(
    () => new Set((data?.entries ?? []).filter((e) => e.categoryId).map((e) => `${e.subStepId}-${e.pass}`)),
    [data],
  )
  const hasSaved = !!data?.step

  // (Re)initialise the draft when the tile or the step changes.
  const initKey = data && tile ? `${data.step?.id ?? 'new'}|${data.industrySectorId}|${tile.key}` : ''
  useEffect(() => {
    if (!initKey || !tile || !data) return
    const d = draftFor(tile, data.entries)
    setDraft(d)
    setInitial(d)
    setErrors({})
    setServerError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initKey])

  const nameDirty = hasSaved ? name.trim() !== data!.step!.name : false
  const tileDirty = normalize(draft, tile) !== normalize(initial, tile)
  const dirty = tileDirty || nameDirty || (!hasSaved && !!name.trim() && tileDirty)
  useUnsavedGuard(dirty)

  const tileRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    tileRef.current?.querySelector<HTMLElement>('[data-active="true"]')?.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' })
  }, [index, tiles.length])

  const goTo = (i: number) => {
    if (i < 0 || i >= tiles.length || i === index) return
    if (tileDirty && !window.confirm(`You have unsaved changes in ${tile?.label} ${tile?.sub.shortName}. Discard them?`)) return
    setIndex(i)
  }

  const changeSector = (v: number) => {
    if (tileDirty && !window.confirm('Changing the industry sector discards the unsaved sub step. Continue?')) return
    setSectorId(v)
    setIndex(0)
  }

  const save = useMutation({
    mutationFn: () => {
      const selections = tile!.sub.pullDowns.map((pd) => {
        const p = draft[pd.id]
        const cat = pd.categories.find((c) => c.id === p?.categoryId)
        return {
          pullDownId: pd.id,
          categoryId: cat ? cat.id : null,
          values: (cat?.characteristics ?? [])
            .map((c) => {
              const v = p?.values[c.id]
              if (c.inputType === 'Material') return v?.materialId ? { characteristicId: c.id, materialId: v.materialId } : null
              return v?.value?.trim() ? { characteristicId: c.id, value: v.value.trim() } : null
            })
            .filter(Boolean),
        }
      })
      return api.put<{ message: string; stepId: number; created: boolean }>('/process-steps/save-substep', {
        stepId: stepId ?? data?.step?.id ?? undefined,
        groupId: data?.groupId ?? groupId,
        industrySectorId: data?.industrySectorId ?? sectorId,
        name: name.trim(),
        subStepId: tile!.sub.id,
        pass: tile!.pass,
        selections,
      })
    },
    onSuccess: (res) => {
      toast.success(res.data.message)
      setInitial(draft)
      qc.invalidateQueries({ queryKey: ['process-step-builder'] })
      qc.invalidateQueries({ queryKey: ['process-steps'] })
      qc.invalidateQueries({ queryKey: ['process-step-view'] })
      if (!stepId) navigate(`/process-steps/${res.data.stepId}`, { replace: true })
      if (index < tiles.length - 1) setIndex(index + 1)
    },
    onError: (e) => {
      const msg = errorMessage(e)
      setServerError(msg)
      toast.error(msg)
    },
  })

  const submit = () => {
    if (!tile) return
    const errs: Record<string, string> = {}
    for (const pd of tile.sub.pullDowns) {
      const p = draft[pd.id]
      const cat = pd.categories.find((c) => c.id === p?.categoryId)
      cat?.characteristics.forEach((c) => {
        const v = p?.values[c.id]?.value?.trim()
        if (c.inputType === 'Number' && v && !isNumber(v)) errs[`${pd.id}-${c.id}`] = 'Enter a number.'
      })
    }
    setErrors(errs)
    if (!name.trim()) {
      setNameError('Step Name is required.')
      return
    }
    setNameError(null)
    if (Object.keys(errs).length === 0) save.mutate()
  }

  const setPull = (pdId: number, next: PullDraft) => setDraft((d) => ({ ...d, [pdId]: next }))

  const chooseCategory = (pd: BuilderPullDown, categoryId: number | null) => {
    const cat = pd.categories.find((c) => c.id === categoryId)
    const prev = draft[pd.id]
    const values: Record<number, ValueDraft> = {}
    // Keep values the user already typed for this category; prefill defaults otherwise.
    cat?.characteristics.forEach((c) => {
      const kept = prev?.categoryId === categoryId ? prev.values[c.id] : undefined
      values[c.id] = kept ?? { value: c.inputType === 'Material' ? '' : c.defaultValue ?? '', materialId: null }
    })
    setPull(pd.id, { categoryId, values })
  }

  const title = stepId ? 'Edit Process Step' : 'Create Process Step'
  const sectors = lookups.data?.industrySectors ?? []
  const savedMaterialNames = useMemo(() => {
    const m = new Map<number, string>()
    data?.entries.forEach((e) => e.values.forEach((v) => v.materialId && v.materialName && m.set(v.materialId, v.materialName)))
    return m
  }, [data])

  if (q.isError && !data)
    return (
      <>
        <PageHeader title={title} breadcrumbs={[['Process Step List', '/process-steps'], title]} />
        <ErrorBanner message={errorMessage(q.error)} />
        <button className="btn-secondary" onClick={() => navigate('/process-steps')}><ArrowLeft className="h-4 w-4" /> Back</button>
      </>
    )

  return (
    <>
      <PageHeader
        title={title}
        subtitle={data?.step ? data.step.name : undefined}
        breadcrumbs={[['Process Step List', '/process-steps'], title]}
        actions={
          <>
            <button className="btn-secondary" disabled={!hasSaved} onClick={() => setDocsOpen(true)} title={hasSaved ? 'Documents' : 'Save a sub step first'}>
              <FileText className="h-4 w-4" /> + Documents
            </button>
            <button className="btn-secondary" disabled={!hasSaved} onClick={() => setViewOpen(true)} title={hasSaved ? 'View Step' : 'Save a sub step first'}>
              <Eye className="h-4 w-4" /> View Step
            </button>
            <button className="btn-ghost" onClick={() => confirmLeave(dirty) && navigate('/process-steps')}>
              <ArrowLeft className="h-4 w-4" /> Back
            </button>
          </>
        }
      />

      {!data ? (
        <LoadingBlock />
      ) : (
        <>
          <div className="card p-4 mb-4 grid gap-4 md:grid-cols-3">
            <Field label="Step Name" required error={nameError}>
              <input
                className={clsx('input', nameError && 'input-invalid')}
                value={name}
                maxLength={400}
                placeholder="e.g. Topcoat Spray - Kemvar CV"
                onChange={(e) => {
                  setName(e.target.value)
                  if (e.target.value.trim()) setNameError(null)
                }}
              />
            </Field>
            <Field label="Group">
              <input className="input" value={data.groupName ?? group?.name ?? ''} disabled readOnly />
            </Field>
            <Field label="Industry Sector" hint={hasSaved ? 'Fixed once a sub step has been saved.' : undefined}>
              <select className="input" value={data.industrySectorId} disabled={hasSaved} onChange={(e) => changeSector(Number(e.target.value))}>
                {sectors.map((s) => (
                  <option key={s.id} value={s.id}>{s.name}</option>
                ))}
              </select>
            </Field>
          </div>

          {tiles.length === 0 ? (
            <div className="card">
              <EmptyState
                icon={<Layers className="h-5 w-5" />}
                title="This industry sector has no sub steps"
                description="A System Administrator configures sub steps in Administration › Sub Step Setup."
              />
            </div>
          ) : (
            <>
              {/* Sub step carousel */}
              <div className="card mb-4 p-2 flex items-stretch gap-1">
                <button className="btn-icon h-auto shrink-0" onClick={() => goTo(index - 1)} disabled={index === 0} aria-label="Previous sub step">
                  <ChevronLeft className="h-5 w-5" />
                </button>
                <div ref={tileRef} className="flex-1 flex gap-2 overflow-x-auto scroll-smooth py-1 px-0.5 [scrollbar-width:thin]">
                  {tiles.map((t, i) => {
                    const active = i === index
                    const filled = filledKeys.has(t.key)
                    return (
                      <button
                        key={t.key}
                        data-active={active}
                        onClick={() => goTo(i)}
                        title={`${t.label} ${t.sub.name}`}
                        className={clsx(
                          'relative shrink-0 w-[104px] h-[72px] rounded-lg border px-2 py-1.5 text-left transition-all',
                          active ? 'bg-primary border-primary text-primary-foreground shadow-md' : 'bg-card hover:border-primary/60 hover:bg-accent/40',
                        )}
                      >
                        <div className={clsx('text-[11px] leading-tight truncate', active ? 'text-primary-foreground/90' : 'text-muted-foreground')}>{t.sub.shortName}</div>
                        <div className="mt-1 text-2xl font-bold leading-none tabular-nums">{t.label}</div>
                        {filled && (
                          <span
                            className={clsx('absolute top-1.5 right-1.5 h-4 w-4 rounded-full grid place-items-center', active ? 'bg-white text-primary' : 'bg-success text-white')}
                            title="Filled"
                          >
                            <Check className="h-3 w-3" strokeWidth={3} />
                          </span>
                        )}
                      </button>
                    )
                  })}
                </div>
                <button className="btn-icon h-auto shrink-0" onClick={() => goTo(index + 1)} disabled={index >= tiles.length - 1} aria-label="Next sub step">
                  <ChevronRight className="h-5 w-5" />
                </button>
              </div>

              {tile && (
                <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_340px]">
                  <section className="card">
                    <div className="flex flex-wrap items-center justify-between gap-2 border-b px-4 py-3">
                      <div className="min-w-0">
                        <div className="text-xs text-muted-foreground">
                          Sub step {tile.label}
                          {tile.sub.passThroughs > 1 && ` · pass ${tile.pass} of ${tile.sub.passThroughs}`} · {index + 1} / {tiles.length}
                        </div>
                        <h2 className="font-semibold">{tile.sub.name}</h2>
                      </div>
                      {tileDirty && <span className="badge bg-amber-100 text-amber-800">Unsaved changes</span>}
                    </div>
                    <div className="p-4 space-y-6">
                      <ErrorBanner message={serverError} />
                      {tile.sub.pullDowns.length === 0 && (
                        <p className="text-sm text-muted-foreground">This sub step has no pull downs configured. A System Administrator can add them in Sub Step Setup.</p>
                      )}
                      {tile.sub.pullDowns.map((pd) => (
                        <PullDownInput
                          key={pd.id}
                          pd={pd}
                          draft={draft[pd.id] ?? { categoryId: null, values: {} }}
                          errors={errors}
                          groupId={data.groupId}
                          savedMaterialNames={savedMaterialNames}
                          onCategory={(cid) => chooseCategory(pd, cid)}
                          onValue={(cid, v) => {
                            const cur = draft[pd.id] ?? { categoryId: null, values: {} }
                            setPull(pd.id, { ...cur, values: { ...cur.values, [cid]: v } })
                            if (errors[`${pd.id}-${cid}`]) setErrors((e) => ({ ...e, [`${pd.id}-${cid}`]: '' }))
                          }}
                        />
                      ))}
                    </div>
                    <div className="flex flex-wrap items-center justify-between gap-2 border-t px-4 py-3 bg-muted/40 rounded-b-lg">
                      <span className="text-xs text-muted-foreground">
                        {!hasSaved ? 'Saving the first sub step creates the process step.' : 'Saving moves you to the next sub step.'}
                      </span>
                      <button className="btn-primary" onClick={submit} disabled={save.isPending}>
                        {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save Sub-step
                      </button>
                    </div>
                  </section>

                  <aside className="space-y-4">
                    <div className="rounded-lg border border-primary/30 bg-accent p-4">
                      <div className="flex items-center gap-2 font-semibold text-accent-foreground">
                        <Info className="h-4 w-4" /> Instructions
                      </div>
                      <p className="mt-2 text-sm whitespace-pre-line">
                        {tile.sub.instruction || <span className="text-muted-foreground">No instructions for this sub step.</span>}
                      </p>
                      {tile.sub.webLink && (
                        <a href={tile.sub.webLink} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline break-all">
                          <ExternalLink className="h-3.5 w-3.5 shrink-0" /> {tile.sub.webLink}
                        </a>
                      )}
                    </div>
                    <div className="card p-4 text-xs text-muted-foreground space-y-1">
                      <div className="font-semibold text-foreground text-sm mb-1">Progress</div>
                      <div>
                        {tiles.filter((t) => filledKeys.has(t.key)).length} of {tiles.length} sub steps filled
                      </div>
                      <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                        <div className="h-full bg-primary" style={{ width: `${(tiles.filter((t) => filledKeys.has(t.key)).length / tiles.length) * 100}%` }} />
                      </div>
                    </div>
                  </aside>
                </div>
              )}
            </>
          )}
        </>
      )}

      {hasSaved && data?.step && (
        <>
          <ViewStepModal open={viewOpen} onClose={() => setViewOpen(false)} stepId={data.step.id} title={data.step.name} />
          {docsOpen && (
            <EntityDocuments open onClose={() => setDocsOpen(false)} entityType="ProcessStep" entityId={data.step.id} groupId={data.groupId} title={`Documents — ${data.step.name}`} />
          )}
        </>
      )}
    </>
  )
}

// ---------------------------------------------------------------------------

function PullDownInput({ pd, draft, errors, groupId, savedMaterialNames, onCategory, onValue }: {
  pd: BuilderPullDown
  draft: PullDraft
  errors: Record<string, string>
  groupId: number
  savedMaterialNames: Map<number, string>
  onCategory: (id: number | null) => void
  onValue: (characteristicId: number, v: ValueDraft) => void
}) {
  const cat = pd.categories.find((c) => c.id === draft.categoryId)
  return (
    <div>
      <Field label={pd.header} hint={pd.categories.length === 0 ? `No material categories in this group match: ${pd.query}.` : undefined}>
        <SearchSelect
          options={pd.categories.map((c) => ({ value: c.id, label: c.name, sub: `${c.materialTypeLabel}${c.filter2 ? ` · ${c.filter2}` : ''}` }))}
          value={draft.categoryId}
          onChange={onCategory}
          placeholder={`Select ${pd.choiceName || 'category'}…`}
          emptyText="No matching categories"
        />
      </Field>
      {cat && (
        <div className="mt-3 rounded-lg border bg-muted/30 p-3 sm:p-4">
          {cat.characteristics.length === 0 ? (
            <p className="text-sm text-muted-foreground">This category has no characteristics. Add them in Material Categories to capture values.</p>
          ) : (
            <div className="grid gap-x-4 gap-y-3 sm:grid-cols-2">
              {cat.characteristics.map((c) => (
                <CharacteristicInput
                  key={c.id}
                  c={c}
                  category={cat}
                  groupId={groupId}
                  value={draft.values[c.id] ?? { value: '', materialId: null }}
                  error={errors[`${pd.id}-${c.id}`]}
                  savedName={draft.values[c.id]?.materialId ? savedMaterialNames.get(draft.values[c.id].materialId!) : undefined}
                  onChange={(v) => onValue(c.id, v)}
                />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function CharacteristicInput({ c, category, groupId, value, error, savedName, onChange }: {
  c: CharacteristicDef
  category: BuilderCategory
  groupId: number
  value: ValueDraft
  error?: string
  savedName?: string
  onChange: (v: ValueDraft) => void
}) {
  const label = (
    <>
      {c.name}
      {c.calcVariable && <span className="ml-1 font-normal text-muted-foreground" title="Used by Material Quantities / Pricing">·ƒ</span>}
    </>
  )
  switch (c.inputType) {
    case 'Number':
      return (
        <Field label={label} error={error}>
          <div className="relative">
            <input
              className={clsx('input', c.unit && 'pr-24', error && 'input-invalid')}
              inputMode="decimal"
              value={value.value}
              placeholder={c.defaultValue ?? ''}
              onChange={(e) => onChange({ ...value, value: e.target.value })}
            />
            {c.unit && <span className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-muted-foreground pointer-events-none max-w-[5.5rem] truncate">{c.unit}</span>}
          </div>
        </Field>
      )
    case 'Material':
      return (
        <Field label={label} className="sm:col-span-2">
          <MaterialPicker category={category} groupId={groupId} value={value.materialId} savedName={savedName} onChange={(id) => onChange({ ...value, materialId: id })} />
        </Field>
      )
    case 'YesNo':
      return (
        <Field label={label}>
          <div className="inline-flex rounded-md border p-0.5 bg-card">
            {['Yes', 'No'].map((o) => (
              <button
                key={o}
                type="button"
                className={clsx('px-4 h-8 rounded text-sm font-medium transition-colors', value.value === o ? 'bg-primary text-primary-foreground' : 'hover:bg-muted')}
                onClick={() => onChange({ ...value, value: value.value === o ? '' : o })}
                aria-pressed={value.value === o}
              >
                {o}
              </button>
            ))}
          </div>
        </Field>
      )
    case 'Notes':
      return (
        <Field label={label} className="sm:col-span-2">
          <textarea className="input" rows={3} maxLength={4000} value={value.value} onChange={(e) => onChange({ ...value, value: e.target.value })} />
        </Field>
      )
    default:
      return (
        <Field label={label}>
          <div className="relative">
            <input className={clsx('input', c.unit && 'pr-24')} value={value.value} maxLength={4000} onChange={(e) => onChange({ ...value, value: e.target.value })} />
            {c.unit && <span className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-muted-foreground pointer-events-none">{c.unit}</span>}
          </div>
        </Field>
      )
  }
}

type Scope = 'category' | 'type' | 'all'

/** Materials of the chosen category first; widen to the whole material type or every material of the group. */
function MaterialPicker({ category, groupId, value, savedName, onChange }: {
  category: BuilderCategory
  groupId: number
  value: number | null
  savedName?: string
  onChange: (id: number | null) => void
}) {
  const [scope, setScope] = useState<Scope>('category')
  const byCategory = useMaterials(groupId, { categoryId: category.id }, scope === 'category')
  const byType = useMaterials(groupId, { type: category.materialType }, scope === 'type')
  const all = useQuery({
    queryKey: ['materials', groupId, 'all'],
    queryFn: () => api.get<{ id: number; productName: string; productCode?: string | null; materialType: number; categoryName?: string | null }[]>('/materials', { params: { groupId } }).then((r) => r.data),
    enabled: scope === 'all',
    staleTime: 60_000,
  })
  const q = scope === 'category' ? byCategory : scope === 'type' ? byType : all
  const list = q.data ?? []
  const options = list.map((m) => ({
    value: m.id,
    label: m.productCode ? `${m.productName} (${m.productCode})` : m.productName,
    sub: [materialTypeLabel[m.materialType], m.categoryName].filter(Boolean).join(' · '),
  }))
  if (value && !options.some((o) => o.value === value)) options.unshift({ value, label: savedName ?? `Material #${value}`, sub: 'Currently selected' })

  // Widen automatically when the category has no materials of its own.
  useEffect(() => {
    if (scope === 'category' && byCategory.data && byCategory.data.length === 0) setScope('type')
  }, [scope, byCategory.data])

  return (
    <div>
      <SearchSelect options={options} value={value} onChange={onChange} placeholder={q.isLoading ? 'Loading materials…' : 'Select material…'} emptyText="No materials found" />
      <div className="mt-1.5 flex flex-wrap items-center gap-1 text-xs">
        <span className="text-muted-foreground mr-1">Show:</span>
        {(
          [
            ['category', 'This category'],
            ['type', `All ${materialTypeLabel[category.materialType] ?? ''}`],
            ['all', 'All materials'],
          ] as [Scope, string][]
        ).map(([k, l]) => (
          <button
            key={k}
            type="button"
            onClick={() => setScope(k)}
            className={clsx('rounded-full px-2 py-0.5 border', scope === k ? 'border-primary bg-accent text-accent-foreground font-medium' : 'hover:bg-muted text-muted-foreground')}
          >
            {l}
          </button>
        ))}
      </div>
    </div>
  )
}
