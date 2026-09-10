import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, ListTree, Pencil, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useLookups } from '@/lib/auth'
import { useToast } from '@/components/toast'
import { DataTable, type Column } from '@/components/DataTable'
import { ConfirmDialog, ErrorBanner, Field, Modal, PageHeader, Spinner, Tabs } from '@/components/ui'
import { materialTypeLabel } from '@/lib/types'
import type { PullDown, SubStep } from './types'

type Role = 'user' | 'admin'
const roleFromHash = (): Role => (location.hash === '#admin' ? 'admin' : 'user')

export default function SubStepsPage() {
  const lookups = useLookups()
  const sectors = useMemo(() => lookups.data?.industrySectors ?? [], [lookups.data])
  const [sectorId, setSectorId] = useState<number>(0)
  const [role, setRole] = useState<Role>(roleFromHash)
  const [editing, setEditing] = useState<SubStep | 'new' | null>(null)
  const [deleting, setDeleting] = useState<SubStep | null>(null)
  const [pullDownsFor, setPullDownsFor] = useState<SubStep | null>(null)
  const toast = useToast()
  const qc = useQueryClient()

  useEffect(() => {
    if (!sectorId && sectors.length) setSectorId(sectors.find((s) => s.name === 'Wood')?.id ?? sectors[0].id)
  }, [sectors, sectorId])

  useEffect(() => {
    const onHash = () => setRole(roleFromHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const changeRole = (r: Role) => {
    setRole(r)
    history.replaceState(null, '', `#${r}`)
  }

  const userRole = role === 'admin' ? 'Admin' : 'User'
  const q = useQuery({
    queryKey: ['sub-steps', sectorId],
    queryFn: () => api.get<SubStep[]>('/sub-steps', { params: { industrySectorId: sectorId } }).then((r) => r.data),
    enabled: sectorId > 0,
  })
  const all = q.data ?? []
  const rows = all.filter((s) => s.userRole === userRole)

  const del = useMutation({
    mutationFn: (id: number) => api.delete(`/sub-steps/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['sub-steps'] })
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setDeleting(null)
    },
  })

  const columns: Column<SubStep>[] = [
    { key: 'id', header: '#', className: 'w-16 text-muted-foreground' },
    { key: 'sequence', header: 'Step Sequence', className: 'w-32' },
    { key: 'shortName', header: 'Short Name', cell: (r) => <span className="font-medium">{r.shortName}</span> },
    { key: 'name', header: 'Name', hideBelow: 'md' },
    { key: 'passThroughs', header: 'Number of "pass throughs"', align: 'center', hideBelow: 'sm' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) => (
        <div className="flex justify-end gap-1">
          <button className="btn-ghost btn-sm" title="Pull Downs" onClick={() => setPullDownsFor(r)}>
            <ListTree className="h-4 w-4" /> Pull Downs
            <span className="badge bg-muted text-muted-foreground">{r.pullDownCount}</span>
          </button>
          <button className="btn-icon" title="Edit" onClick={() => setEditing(r)}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(r)}>
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ]

  const nextSequence = all.reduce((m, s) => Math.max(m, s.sequence), 0) + 1

  return (
    <>
      <PageHeader
        title="Process Sub Step Setup"
        breadcrumbs={['Administration', 'Process Sub Step Setup']}
        actions={
          <button className="btn-primary" onClick={() => setEditing('new')} disabled={!sectorId}>
            <Plus className="h-4 w-4" /> New Sub Step
          </button>
        }
      />
      <div className="card p-4 mb-4 flex flex-wrap items-end gap-4">
        <Field label="Industry Sector" className="w-full sm:w-72">
          <select className="input" value={sectorId || ''} onChange={(e) => setSectorId(Number(e.target.value))}>
            {sectors.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </Field>
        <p className="text-xs text-muted-foreground max-w-xl pb-2">
          <b>User</b> sub steps are the tiles of the process step builder. <b>Admin</b> sub steps are part of every step and appear in
          "View Step" but are not edited in the builder.
        </p>
      </div>
      <Tabs
        className="mb-3"
        tabs={[
          { key: 'user', label: 'User', count: all.filter((s) => s.userRole === 'User').length },
          { key: 'admin', label: 'Admin', count: all.filter((s) => s.userRole === 'Admin').length },
        ]}
        value={role}
        onChange={changeRole}
      />
      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={q.isLoading || !sectorId}
        searchPlaceholder="Search sub steps…"
        initialSort={{ key: 'sequence', dir: 'asc' }}
        emptyTitle={`No ${userRole} sub steps for this industry sector`}
        emptyAction={<button className="btn-primary btn-sm" onClick={() => setEditing('new')}><Plus className="h-4 w-4" /> New Sub Step</button>}
      />
      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}

      <SubStepModal
        open={editing !== null}
        subStep={editing === 'new' ? null : editing}
        sectorId={sectorId}
        defaultRole={userRole}
        defaultSequence={nextSequence}
        onClose={() => setEditing(null)}
      />
      <PullDownsModal subStep={pullDownsFor} onClose={() => setPullDownsFor(null)} />
      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.name}" sub step?`}
        busy={del.isPending}
        onConfirm={() => deleting && del.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

// ---------------------------------------------------------------------------

interface SubStepForm {
  userRole: string
  name: string
  shortName: string
  sequence: string
  passThroughs: string
  webLink: string
  instruction: string
}

function SubStepModal({ open, subStep, sectorId, defaultRole, defaultSequence, onClose }: {
  open: boolean
  subStep: SubStep | null
  sectorId: number
  defaultRole: string
  defaultSequence: number
  onClose: () => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [f, setF] = useState<SubStepForm>(() => blank())
  const [errors, setErrors] = useState<Partial<Record<keyof SubStepForm, string>>>({})
  const [serverError, setServerError] = useState<string | null>(null)

  function blank(): SubStepForm {
    return { userRole: defaultRole, name: '', shortName: '', sequence: String(defaultSequence), passThroughs: '1', webLink: '', instruction: '' }
  }

  useEffect(() => {
    if (!open) return
    setErrors({})
    setServerError(null)
    setF(
      subStep
        ? {
            userRole: subStep.userRole,
            name: subStep.name,
            shortName: subStep.shortName,
            sequence: String(subStep.sequence),
            passThroughs: String(subStep.passThroughs),
            webLink: subStep.webLink ?? '',
            instruction: subStep.instruction ?? '',
          }
        : blank(),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, subStep])

  const save = useMutation({
    mutationFn: () => {
      const body = {
        industrySectorId: subStep?.industrySectorId ?? sectorId,
        userRole: f.userRole,
        name: f.name.trim(),
        shortName: f.shortName.trim(),
        sequence: Number(f.sequence),
        passThroughs: Number(f.passThroughs),
        webLink: f.webLink.trim() || null,
        instruction: f.instruction.trim() || null,
      }
      return subStep ? api.put(`/sub-steps/${subStep.id}`, body) : api.post('/sub-steps', body)
    },
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['sub-steps'] })
      onClose()
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const submit = () => {
    const e: typeof errors = {}
    if (!f.name.trim()) e.name = 'Name is required.'
    if (!f.shortName.trim()) e.shortName = 'Short Name is required.'
    if (!/^\d+$/.test(f.sequence) || Number(f.sequence) < 1) e.sequence = 'Enter a whole number of 1 or more.'
    if (!/^\d+$/.test(f.passThroughs) || Number(f.passThroughs) < 1) e.passThroughs = 'Enter a whole number of 1 or more.'
    else if (Number(f.passThroughs) > 26) e.passThroughs = 'At most 26 pass throughs (A–Z).'
    if (f.webLink.trim() && !/^https?:\/\//i.test(f.webLink.trim())) e.webLink = 'Enter a full URL starting with http:// or https://'
    setErrors(e)
    setServerError(null)
    if (Object.keys(e).length === 0) save.mutate()
  }

  const set = (k: keyof SubStepForm) => (v: string) => setF((x) => ({ ...x, [k]: v }))
  const passes = Number(f.passThroughs) || 1

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={subStep ? `Edit Sub Step (${subStep.name})` : 'Create New Sub Step'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <ErrorBanner message={serverError} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="User Role" required>
          <select className="input" value={f.userRole} onChange={(e) => set('userRole')(e.target.value)}>
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>
        </Field>
        <Field label="Sequence" required error={errors.sequence} hint="Unique within the industry sector.">
          <input className="input" inputMode="numeric" value={f.sequence} onChange={(e) => set('sequence')(e.target.value)} />
        </Field>
        <Field label="Name" required error={errors.name} className="sm:col-span-2">
          <input className="input" value={f.name} onChange={(e) => set('name')(e.target.value)} maxLength={400} autoFocus />
        </Field>
        <Field label="Short Name" required error={errors.shortName} hint="Shown on the step builder tiles.">
          <input className="input" value={f.shortName} onChange={(e) => set('shortName')(e.target.value)} maxLength={400} />
        </Field>
        <Field
          label="Number of Pass Throughs"
          required
          error={errors.passThroughs}
          hint={passes > 1 ? `Tiles ${Array.from({ length: Math.min(passes, 26) }, (_, i) => `${f.sequence || '#'}${String.fromCharCode(65 + i)}`).join(', ')}` : 'One tile.'}
        >
          <input className="input" inputMode="numeric" value={f.passThroughs} onChange={(e) => set('passThroughs')(e.target.value)} />
        </Field>
        <Field label="WebLink" error={errors.webLink} className="sm:col-span-2">
          <input className="input" value={f.webLink} placeholder="https://" onChange={(e) => set('webLink')(e.target.value)} maxLength={400} />
        </Field>
        <Field label="Instruction" hint='Shown in the builder "Instructions" box.' className="sm:col-span-2">
          <textarea className="input" rows={4} value={f.instruction} onChange={(e) => set('instruction')(e.target.value)} maxLength={4000} />
        </Field>
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Pull downs
// ---------------------------------------------------------------------------

function PullDownsModal({ subStep, onClose }: { subStep: SubStep | null; onClose: () => void }) {
  const toast = useToast()
  const qc = useQueryClient()
  const [editing, setEditing] = useState<PullDown | 'new' | null>(null)
  const [deleting, setDeleting] = useState<PullDown | null>(null)
  const q = useQuery({
    queryKey: ['sub-step-pull-downs', subStep?.id],
    queryFn: () => api.get<PullDown[]>(`/sub-steps/${subStep!.id}/pull-downs`).then((r) => r.data),
    enabled: !!subStep,
  })
  const rows = q.data ?? []

  const del = useMutation({
    mutationFn: (id: number) => api.delete(`/sub-steps/pull-downs/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['sub-step-pull-downs'] })
      qc.invalidateQueries({ queryKey: ['sub-steps'] })
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      setDeleting(null)
    },
  })

  const columns: Column<PullDown>[] = [
    { key: 'id', header: '#', className: 'w-14 text-muted-foreground' },
    { key: 'sequence', header: 'Control Sequence', className: 'w-24' },
    { key: 'choiceName', header: 'Choice Name', hideBelow: 'sm' },
    { key: 'header', header: 'Header', cell: (r) => <span className="font-medium">{r.header}</span> },
    { key: 'query', header: 'Query', cell: (r) => <code className="text-xs text-muted-foreground">{r.query}</code>, hideBelow: 'md' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (r) => (
        <div className="flex justify-end gap-1">
          <button className="btn-icon" title="Edit" onClick={() => setEditing(r)}><Pencil className="h-4 w-4" /></button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(r)}><Trash2 className="h-4 w-4" /></button>
        </div>
      ),
    },
  ]

  const nextSeq = rows.reduce((m, p) => Math.max(m, p.sequence), 0) + 1

  return (
    <>
      <Modal
        open={!!subStep && editing === null && deleting === null}
        onClose={onClose}
        size="xl"
        title={`Pull Downs — ${subStep?.sequence} ${subStep?.name}`}
        footer={<button className="btn-secondary" onClick={onClose}>Close</button>}
      >
        <DataTable
          bare
          dense
          rows={rows}
          columns={columns}
          rowKey={(r) => r.id}
          loading={q.isLoading}
          initialSort={{ key: 'sequence', dir: 'asc' }}
          emptyTitle="No pull downs yet"
          emptyDescription="A pull down lets the step builder pick a material category; its characteristics become the inputs."
          toolbar={
            <button className="btn-primary btn-sm" onClick={() => setEditing('new')}>
              <Plus className="h-4 w-4" /> Create New Pull Down
            </button>
          }
        />
      </Modal>
      {subStep && (
        <PullDownModal
          open={editing !== null}
          subStepId={subStep.id}
          pullDown={editing === 'new' ? null : editing}
          defaultSequence={nextSeq}
          onClose={() => setEditing(null)}
        />
      )}
      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.header}" pull down?`}
        busy={del.isPending}
        onConfirm={() => deleting && del.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

function PullDownModal({ open, subStepId, pullDown, defaultSequence, onClose }: {
  open: boolean
  subStepId: number
  pullDown: PullDown | null
  defaultSequence: number
  onClose: () => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const lookups = useLookups()
  const [f, setF] = useState({ sequence: '', header: '', choiceName: '', materialType: '', filter1: '', filter2: '' })
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setError(null)
    setF(
      pullDown
        ? {
            sequence: String(pullDown.sequence),
            header: pullDown.header,
            choiceName: pullDown.choiceName ?? '',
            materialType: pullDown.materialType ? String(pullDown.materialType) : '',
            filter1: pullDown.categoryFilter1 ?? '',
            filter2: pullDown.categoryFilter2 ?? '',
          }
        : { sequence: String(defaultSequence), header: '', choiceName: '', materialType: '', filter1: '', filter2: '' },
    )
  }, [open, pullDown, defaultSequence])

  const preview = useMemo(() => {
    const parts: string[] = []
    if (f.materialType) parts.push(`Type = ${materialTypeLabel[Number(f.materialType)]}`)
    if (f.filter1.trim()) parts.push(`Filter1 = '${f.filter1.trim()}'`)
    if (f.filter2.trim()) parts.push(`Filter2 = '${f.filter2.trim()}'`)
    return parts.length ? `Categories where ${parts.join(' and ')}` : 'All categories'
  }, [f])

  const save = useMutation({
    mutationFn: () => {
      const body = {
        sequence: Number(f.sequence),
        header: f.header.trim(),
        choiceName: f.choiceName.trim() || null,
        materialType: f.materialType ? Number(f.materialType) : null,
        categoryFilter1: f.filter1.trim() || null,
        categoryFilter2: f.filter2.trim() || null,
      }
      return pullDown ? api.put(`/sub-steps/pull-downs/${pullDown.id}`, body) : api.post(`/sub-steps/${subStepId}/pull-downs`, body)
    },
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['sub-step-pull-downs'] })
      qc.invalidateQueries({ queryKey: ['sub-steps'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    if (!f.header.trim()) return setError('Header is required.')
    if (!/^\d+$/.test(f.sequence) || Number(f.sequence) < 1) return setError('Sequence must be a whole number of 1 or more.')
    setError(null)
    save.mutate()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={pullDown ? 'Edit Pull Down' : 'Create New Pull Down'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending}>{save.isPending && <Spinner />} Save</button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Choice Name">
          <input className="input" value={f.choiceName} onChange={(e) => setF({ ...f, choiceName: e.target.value })} maxLength={400} />
        </Field>
        <Field label="Sequence" required>
          <input className="input" inputMode="numeric" value={f.sequence} onChange={(e) => setF({ ...f, sequence: e.target.value })} />
        </Field>
        <Field label="Header" required hint="The question shown above the category dropdown in the step builder." className="sm:col-span-2">
          <input className="input" value={f.header} onChange={(e) => setF({ ...f, header: e.target.value })} maxLength={400} />
        </Field>
      </div>
      <fieldset className="mt-5 rounded-md border p-4">
        <legend className="px-1 text-xs font-semibold text-foreground/80">Query — which material categories are offered</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field label="Material Type">
            <select className="input" value={f.materialType} onChange={(e) => setF({ ...f, materialType: e.target.value })}>
              <option value="">Any type</option>
              {(lookups.data?.materialTypes ?? []).map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </Field>
          <Field label="Category Filter1">
            <input className="input" value={f.filter1} placeholder="Any" onChange={(e) => setF({ ...f, filter1: e.target.value })} maxLength={400} />
          </Field>
          <Field label="Category Filter2">
            <input className="input" value={f.filter2} placeholder="Any, e.g. GUNS" onChange={(e) => setF({ ...f, filter2: e.target.value })} maxLength={400} />
          </Field>
        </div>
        <div className="mt-3 rounded bg-muted px-3 py-2 text-xs">
          <code>{preview}</code>
        </div>
        <p className="mt-2 flex items-center gap-1 text-xs text-muted-foreground">
          <ExternalLink className="h-3 w-3" /> Filters match the Filter1/Filter2 columns of Material Categories (case-insensitive) in the step's group.
        </p>
      </fieldset>
    </Modal>
  )
}
