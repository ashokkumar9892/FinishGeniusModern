import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, ListTree, Pencil, Plus, Save, SlidersHorizontal, Tags, Trash2, Undo2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useVisibleTab } from '@/lib/access'
import { useGroup, useLookups } from '@/lib/auth'
import { MaterialType, materialTypeLabel } from '@/lib/types'
import { DataTable, type Column } from '@/components/DataTable'
import { Card, ConfirmDialog, EmptyState, ErrorBanner, Field, Modal, Note, PageHeader, Spinner, Tabs } from '@/components/ui'
import { useToast } from '@/components/toast'
import { useCategories, type Category, type Characteristic, type MessageResponse } from './shared'

const typeTabs = [
  MaterialType.Base,
  MaterialType.Pigment,
  MaterialType.Dye,
  MaterialType.Equipment,
  MaterialType.Sundry,
  MaterialType.Formula,
  MaterialType.Product,
].map((t) => ({ key: String(t), type: t, label: materialTypeLabel[t] }))

const inputTypeLabels: Record<string, string> = {
  Text: 'Text',
  Number: 'Number',
  Material: 'Material (pick from category)',
  YesNo: 'Yes / No',
  Notes: 'Notes',
}

const numericCalc = ['Coverage', 'MixPercent', 'ProductionRate', 'CostPerSqFt']

export default function MaterialCategoriesPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const { groupId, group } = useGroup()
  const [tab, setTab] = useState(() => {
    const h = location.hash.replace('#', '')
    return typeTabs.some((t) => t.key === h) ? h : String(MaterialType.Base)
  })
  const { allowed: tabAllowed, tab: visibleTab } = useVisibleTab('materialCategories', typeTabs.map((t) => t.key), tab)
  const type = Number(visibleTab ?? tab)
  const categories = useCategories(groupId, type)
  const all = useCategories(groupId)
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [modal, setModal] = useState<{ open: boolean; category: Category | null }>({ open: false, category: null })
  const [deleting, setDeleting] = useState<Category | null>(null)

  useEffect(() => {
    history.replaceState(null, '', `#${tab}`)
    setSelectedId(null)
  }, [tab])
  useEffect(() => setSelectedId(null), [groupId])

  const selected = categories.data?.find((c) => c.id === selectedId) ?? null
  const counts = useMemo(() => {
    const c: Record<string, number> = {}
    for (const cat of all.data ?? []) c[cat.materialType] = (c[cat.materialType] ?? 0) + 1
    return c
  }, [all.data])

  const remove = useMutation({
    mutationFn: (id: number) => api.delete<MessageResponse>(`/material-categories/${id}`).then((r) => r.data),
    onSuccess: (res, id) => {
      toast.success(res.message)
      setDeleting(null)
      if (selectedId === id) setSelectedId(null)
      qc.invalidateQueries({ queryKey: ['material-categories'] })
    },
    onError: (e) => {
      setDeleting(null)
      toast.error(errorMessage(e))
    },
  })

  const columns: Column<Category>[] = [
    {
      key: 'name',
      header: 'Name',
      cell: (c) => (
        <span className={clsx('font-medium', c.id === selectedId && 'text-primary')}>
          {c.name}
        </span>
      ),
    },
    { key: 'filter1', header: 'Filter1', hideBelow: 'lg' },
    { key: 'filter2', header: 'Filter2', hideBelow: 'sm' },
    { key: 'materialCount', header: '# Materials', align: 'right', hideBelow: 'md' },
    { key: 'characteristics', header: '# Characteristics', align: 'right', sortValue: (c) => c.characteristics.length, cell: (c) => c.characteristics.length },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (c) => (
        <div className="flex justify-end gap-0.5">
          <button className={clsx('btn-icon', c.id === selectedId && 'text-primary')} title="Characteristics" onClick={() => setSelectedId(c.id === selectedId ? null : c.id)}>
            <SlidersHorizontal className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Edit" onClick={() => setModal({ open: true, category: c })}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(c)}>
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Material Categories"
        breadcrumbs={['Material Categories']}
        subtitle={group?.name}
        actions={
          <button className="btn-primary" onClick={() => setModal({ open: true, category: null })}>
            <Plus className="h-4 w-4" /> New Category
          </button>
        }
      />
      <Tabs
        className="mb-4"
        value={visibleTab ?? tab}
        onChange={setTab}
        tabs={typeTabs.map((t) => ({ key: t.key, label: t.label, count: counts[t.type] ?? 0, hidden: !tabAllowed(t.key) }))}
      />
      {categories.isError && <ErrorBanner message={errorMessage(categories.error)} />}

      <div className="grid gap-5 xl:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
        <DataTable
          rows={categories.data ?? []}
          columns={columns}
          rowKey={(c) => c.id}
          loading={categories.isLoading}
          searchPlaceholder="Search categories…"
          initialSort={{ key: 'name', dir: 'asc' }}
          onRowClick={(c) => setSelectedId(c.id === selectedId ? null : c.id)}
          rowClassName={(c) => (c.id === selectedId ? 'bg-accent' : undefined)}
          emptyTitle={`No ${materialTypeLabel[type]} categories yet`}
          emptyDescription="Categories group materials and define the inputs (characteristics) shown in process steps."
          emptyAction={
            <button className="btn-primary" onClick={() => setModal({ open: true, category: null })}>
              <Plus className="h-4 w-4" /> New Category
            </button>
          }
        />
        <div className="min-w-0">
          {selected ? (
            <CharacteristicsEditor key={selected.id} category={selected} />
          ) : (
            <div className="card">
              <EmptyState
                icon={<ListTree className="h-5 w-5" />}
                title="Select a category"
                description="Click a category to view and edit its characteristics — the inputs shown when the category is chosen inside a process sub step."
              />
            </div>
          )}
        </div>
      </div>

      <CategoryModal
        open={modal.open}
        onClose={() => setModal({ open: false, category: null })}
        category={modal.category}
        defaultType={type}
        groupId={groupId}
        filterSuggestions={all.data ?? []}
        onSaved={(id, t) => {
          if (t !== type) setTab(String(t))
          setTimeout(() => setSelectedId(id), 0)
        }}
      />
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        message={`Are you sure you want to delete the "${deleting?.name}" category?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </>
  )
}

// ---------------------------------------------------------------------------

function CategoryModal({ open, onClose, category, defaultType, groupId, filterSuggestions, onSaved }: {
  open: boolean
  onClose: () => void
  category: Category | null
  defaultType: number
  groupId: number
  filterSuggestions: Category[]
  onSaved: (id: number, type: number) => void
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [name, setName] = useState('')
  const [type, setType] = useState(defaultType)
  const [filter1, setFilter1] = useState('')
  const [filter2, setFilter2] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [nameError, setNameError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setName(category?.name ?? '')
    setType(category?.materialType ?? defaultType)
    setFilter1(category?.filter1 ?? '')
    setFilter2(category?.filter2 ?? '')
    setError(null)
    setNameError(null)
  }, [open, category, defaultType])

  const f1 = useMemo(() => [...new Set(filterSuggestions.map((c) => c.filter1).filter(Boolean))] as string[], [filterSuggestions])
  const f2 = useMemo(() => [...new Set(filterSuggestions.map((c) => c.filter2).filter(Boolean))].sort() as string[], [filterSuggestions])

  const save = useMutation({
    mutationFn: () => {
      const body = { groupId, name: name.trim(), materialType: type, filter1: filter1.trim() || null, filter2: filter2.trim() || null }
      return (category ? api.put<MessageResponse>(`/material-categories/${category.id}`, body) : api.post<MessageResponse>('/material-categories', body)).then((r) => r.data)
    },
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['material-categories'] })
      qc.invalidateQueries({ queryKey: ['materials'] })
      onClose()
      if (res.id) onSaved(res.id, type)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    if (!name.trim()) {
      setNameError('Name is required.')
      setError('Name is required.')
      return
    }
    setNameError(null)
    setError(null)
    save.mutate()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={category ? 'Edit Category' : 'New Category'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Name" required error={nameError} className="sm:col-span-2">
          <input className={clsx('input', nameError && 'input-invalid')} autoFocus value={name} onChange={(e) => setName(e.target.value)} maxLength={400} onKeyDown={(e) => e.key === 'Enter' && submit()} />
        </Field>
        <Field label="Material Type" required hint={category && category.materialCount > 0 ? 'Locked while materials use this category.' : undefined}>
          <select className="input" value={type} disabled={!!category && category.materialCount > 0} onChange={(e) => setType(Number(e.target.value))}>
            {typeTabs.map((t) => (
              <option key={t.key} value={t.type}>
                {t.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Filter1">
          <input className="input" list="fg-cat-filter1" value={filter1} onChange={(e) => setFilter1(e.target.value)} maxLength={400} />
          <datalist id="fg-cat-filter1">
            {f1.map((v) => (
              <option key={v} value={v} />
            ))}
          </datalist>
        </Field>
        <Field label="Filter2" className="sm:col-span-2" hint='Process sub-step pull-downs list categories by Material Type + Filter2 (e.g. "GUNS", "MATERIALS", "SUNDRIES").'>
          <input className="input" list="fg-cat-filter2" value={filter2} onChange={(e) => setFilter2(e.target.value)} maxLength={400} />
          <datalist id="fg-cat-filter2">
            {f2.map((v) => (
              <option key={v} value={v} />
            ))}
          </datalist>
        </Field>
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------------------

interface Draft {
  name: string
  unit: string
  inputType: string
  calcVariable: string
  defaultValue: string
}

const toDraft = (c?: Characteristic): Draft => ({
  name: c?.name ?? '',
  unit: c?.unit ?? '',
  inputType: c?.inputType ?? 'Text',
  calcVariable: c?.calcVariable ?? '',
  defaultValue: c?.defaultValue ?? '',
})

const same = (a: Draft, b: Draft) => a.name === b.name && a.unit === b.unit && a.inputType === b.inputType && a.calcVariable === b.calcVariable && a.defaultValue === b.defaultValue

/** Picking a calc variable implies the input type it needs. */
function withCalc(d: Draft, calc: string): Draft {
  if (calc === 'Material_ID') return { ...d, calcVariable: calc, inputType: 'Material' }
  if (numericCalc.includes(calc)) return { ...d, calcVariable: calc, inputType: 'Number' }
  return { ...d, calcVariable: calc }
}

function validateDraft(d: Draft): string | null {
  if (!d.name.trim()) return 'Name is required.'
  if (d.calcVariable === 'Material_ID' && d.inputType !== 'Material') return '"Material used" requires the Material input type.'
  if (numericCalc.includes(d.calcVariable) && d.inputType !== 'Number') return 'Numeric calculation variables require the Number input type.'
  if (d.inputType === 'Number' && d.defaultValue.trim() && !Number.isFinite(Number(d.defaultValue))) return 'Default value must be a number.'
  return null
}

function CharacteristicsEditor({ category }: { category: Category }) {
  const qc = useQueryClient()
  const toast = useToast()
  const lookups = useLookups()
  const [drafts, setDrafts] = useState<Record<number, Draft>>({})
  const [adding, setAdding] = useState<Draft | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<number | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Characteristic | null>(null)

  const chars = useMemo(() => [...category.characteristics].sort((a, b) => a.sequence - b.sequence), [category.characteristics])
  const inputTypes = lookups.data?.inputTypes ?? Object.keys(inputTypeLabels)
  const calcVars = lookups.data?.calcVariables ?? [{ value: '', label: 'None' }]
  const calcLabel = (v?: string | null) => calcVars.find((c) => c.value === (v ?? ''))?.label ?? v

  const refresh = () => qc.invalidateQueries({ queryKey: ['material-categories'] })
  const onErr = (e: unknown) => setError(errorMessage(e))

  const update = useMutation({
    mutationFn: ({ id, d }: { id: number; d: Draft }) => api.put<MessageResponse>(`/material-categories/characteristics/${id}`, d).then((r) => r.data),
    onMutate: ({ id }) => setBusy(id),
    onSuccess: (res, { id }) => {
      toast.success(res.message)
      setError(null)
      setDrafts((all) => {
        const next = { ...all }
        delete next[id]
        return next
      })
      refresh()
    },
    onError: onErr,
    onSettled: () => setBusy(null),
  })

  const add = useMutation({
    mutationFn: (d: Draft) => api.post<MessageResponse>(`/material-categories/${category.id}/characteristics`, d).then((r) => r.data),
    onMutate: () => setBusy('new'),
    onSuccess: (res) => {
      toast.success(res.message)
      setError(null)
      setAdding(null)
      refresh()
    },
    onError: onErr,
    onSettled: () => setBusy(null),
  })

  const remove = useMutation({
    mutationFn: (id: number) => api.delete<MessageResponse>(`/material-categories/characteristics/${id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      refresh()
    },
    onError: (e) => {
      setDeleting(null)
      onErr(e)
    },
  })

  const reorder = useMutation({
    mutationFn: (ids: number[]) => api.put<MessageResponse>(`/material-categories/${category.id}/characteristics/order`, { ids }).then((r) => r.data),
    onSuccess: () => refresh(),
    onError: onErr,
  })

  const move = (index: number, dir: -1 | 1) => {
    const ids = chars.map((c) => c.id)
    const j = index + dir
    if (j < 0 || j >= ids.length) return
    ;[ids[index], ids[j]] = [ids[j], ids[index]]
    reorder.mutate(ids)
  }

  const saveRow = (id: number, d: Draft) => {
    const msg = validateDraft(d)
    if (msg) return setError(msg)
    update.mutate({ id, d: { ...d, name: d.name.trim() } })
  }
  const saveNew = () => {
    if (!adding) return
    const msg = validateDraft(adding)
    if (msg) return setError(msg)
    add.mutate({ ...adding, name: adding.name.trim() })
  }

  const renderInputs = (d: Draft, onChange: (d: Draft) => void, onEnter: () => void) => (
    <>
      <td className="td">
        <input className="input h-8 min-w-[10rem]" value={d.name} placeholder="Name" onChange={(e) => onChange({ ...d, name: e.target.value })} onKeyDown={(e) => e.key === 'Enter' && onEnter()} maxLength={400} />
      </td>
      <td className="td">
        <input className="input h-8 w-20" value={d.unit} placeholder="—" onChange={(e) => onChange({ ...d, unit: e.target.value })} maxLength={400} />
      </td>
      <td className="td">
        <select className="input h-8 min-w-[8rem]" value={d.inputType} onChange={(e) => onChange({ ...d, inputType: e.target.value })}>
          {inputTypes.map((t) => (
            <option key={t} value={t}>
              {inputTypeLabels[t] ?? t}
            </option>
          ))}
        </select>
      </td>
      <td className="td">
        <select className="input h-8 min-w-[11rem]" value={d.calcVariable} onChange={(e) => onChange(withCalc(d, e.target.value))} title={calcLabel(d.calcVariable) ?? ''}>
          {calcVars.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
      </td>
      <td className="td">
        <input className="input h-8 w-24" value={d.defaultValue} placeholder="—" onChange={(e) => onChange({ ...d, defaultValue: e.target.value })} onKeyDown={(e) => e.key === 'Enter' && onEnter()} maxLength={400} />
      </td>
    </>
  )

  return (
    <Card
      title={
        <span className="flex items-center gap-2 min-w-0">
          <Tags className="h-4 w-4 text-primary shrink-0" />
          <span className="truncate">{category.name}</span>
          <span className="badge bg-muted text-muted-foreground">{category.materialTypeLabel}</span>
        </span>
      }
      actions={
        <button className="btn-secondary btn-sm" disabled={!!adding} onClick={() => setAdding(toDraft())}>
          <Plus className="h-4 w-4" /> Add characteristic
        </button>
      }
      bodyClassName="p-0"
    >
      <div className="p-4 pb-0">
        <ErrorBanner message={error} />
      </div>
      {chars.length === 0 && !adding ? (
        <EmptyState
          icon={<SlidersHorizontal className="h-5 w-5" />}
          title="No characteristics defined"
          description="Characteristics are the inputs (e.g. Coverage, Mix %, Gun Tip) a user fills in when this category is picked inside a process sub step."
          action={
            <button className="btn-primary" onClick={() => setAdding(toDraft())}>
              <Plus className="h-4 w-4" /> Add characteristic
            </button>
          }
        />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-muted/60 border-y">
              <tr>
                <th className="th w-10">Seq</th>
                <th className="th">Name</th>
                <th className="th">Unit</th>
                <th className="th">Input Type</th>
                <th className="th">Calc Variable</th>
                <th className="th">Default</th>
                <th className="th text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {chars.map((c, i) => {
                const original = toDraft(c)
                const d = drafts[c.id] ?? original
                const dirty = !same(d, original)
                return (
                  <tr key={c.id} className={clsx('border-b', dirty && 'bg-accent/60')}>
                    <td className="td tabular-nums text-muted-foreground">{c.sequence}</td>
                    {renderInputs(d, (nd) => setDrafts((all) => ({ ...all, [c.id]: nd })), () => dirty && saveRow(c.id, d))}
                    <td className="td">
                      <div className="flex justify-end gap-0.5">
                        <button className="btn-icon" title="Move up" disabled={i === 0 || reorder.isPending} onClick={() => move(i, -1)}>
                          <ArrowUp className="h-4 w-4" />
                        </button>
                        <button className="btn-icon" title="Move down" disabled={i === chars.length - 1 || reorder.isPending} onClick={() => move(i, 1)}>
                          <ArrowDown className="h-4 w-4" />
                        </button>
                        {dirty && (
                          <button className="btn-icon" title="Undo changes" onClick={() => setDrafts((all) => { const n = { ...all }; delete n[c.id]; return n })}>
                            <Undo2 className="h-4 w-4" />
                          </button>
                        )}
                        <button className="btn-icon text-success" title="Save" disabled={!dirty || busy === c.id} onClick={() => saveRow(c.id, d)}>
                          {busy === c.id ? <Spinner /> : <Save className="h-4 w-4" />}
                        </button>
                        <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(c)}>
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
              {adding && (
                <tr className="border-b bg-accent/40">
                  <td className="td text-muted-foreground">{chars.length + 1}</td>
                  {renderInputs(adding, setAdding, saveNew)}
                  <td className="td">
                    <div className="flex justify-end gap-1">
                      <button className="btn-ghost btn-sm" onClick={() => setAdding(null)}>
                        Cancel
                      </button>
                      <button className="btn-primary btn-sm" disabled={busy === 'new'} onClick={saveNew}>
                        {busy === 'new' && <Spinner />} Add
                      </button>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
      <div className="p-4">
        <Note tone="info">
          <div className="font-medium mb-1">How calculation variables work</div>
          <p className="text-xs leading-relaxed">
            Calc variables tell <b>Material Quantities</b> and <b>Pricing</b> how to use a characteristic’s value:{' '}
            <b>{calcLabel('Material_ID')}</b> marks the material picked from this category (input type Material); <b>{calcLabel('Coverage')}</b> converts
            the job area into gallons of that material; <b>{calcLabel('MixPercent')}</b> adds a component (catalyst, reducer…) as a percentage of the
            previous material; <b>{calcLabel('ProductionRate')}</b> converts area into labor hours; <b>{calcLabel('CostPerSqFt')}</b> adds a direct
            cost per square foot. Characteristics without a calc variable are informational only.
          </p>
        </Note>
      </div>
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        message={`Are you sure you want to delete the "${deleting?.name}" characteristic?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </Card>
  )
}
