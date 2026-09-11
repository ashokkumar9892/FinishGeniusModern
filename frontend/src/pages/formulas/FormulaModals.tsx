import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { useToast } from '@/components/toast'
import { FORMULA_CATEGORY_TYPES, type CategoryOption } from './types'

export function StatusBadge({ complete }: { complete: boolean }) {
  return (
    <span
      className={clsx(
        'badge',
        complete
          ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-300'
          : 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-300',
      )}
    >
      {complete ? 'Complete' : 'Incomplete'}
    </span>
  )
}

/** "Create New Formula" — Group, Product Category, Name, Number (legacy Create modal); opens the editor afterwards. */
export function CreateFormulaModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { groupId, groups } = useGroup()
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()
  const [gid, setGid] = useState<number | null>(null)
  const [categoryId, setCategoryId] = useState<number | null>(null)
  const [name, setName] = useState('')
  const [number, setNumber] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setGid(groupId > 0 ? groupId : null)
    setCategoryId(null)
    setName('')
    setNumber('')
    setError(null)
  }, [open, groupId])

  const categories = useQuery({
    queryKey: ['material-categories', gid, 'formula'],
    queryFn: () =>
      api.get<CategoryOption[]>('/material-categories', { params: { groupId: gid } })
        .then((r) => r.data.filter((c) => c.materialType == null || FORMULA_CATEGORY_TYPES.includes(c.materialType))),
    enabled: open && !!gid,
  })

  const create = useMutation({
    mutationFn: () =>
      api.post<{ message: string; id: number }>('/formulas', {
        groupId: gid, categoryId, name: name.trim(), number: number.trim(), isComplete: false, batchType: 2, containerPrice: 0, markUp: 0, ingredients: [],
      }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formulas'] })
      onClose()
      navigate(`/formulas/${res.data.id}`)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    const errs = [!gid && 'Group is required.', !name.trim() && 'Name is required.', !number.trim() && 'Number is required.'].filter(Boolean)
    if (errs.length) return setError(errs.join('\n'))
    // Legacy: the backtick key is blocked in formula names.
    if (name.includes('`')) return setError('The ` character is not allowed in a formula name.')
    create.mutate()
  }

  return (
    <Modal
      open={open}
      onClose={() => !create.isPending && onClose()}
      title="Create New Formula"
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={create.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={create.isPending}>
            {create.isPending && <Spinner />} Create
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <div className="space-y-3">
        <Field label="Group" required>
          <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={gid} onChange={(v) => { setGid(v); setCategoryId(null) }} placeholder="Select group" clearable={false} />
        </Field>
        <Field label="Product Category">
          <SearchSelect
            options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            value={categoryId}
            onChange={setCategoryId}
            placeholder={categories.isLoading ? 'Loading…' : 'Select a Category'}
            disabled={!gid}
          />
        </Field>
        <Field label="Name" required>
          <input className="input" value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
        </Field>
        <Field label="Number" required hint="Formula numbers must be unique inside the group.">
          <input className="input" value={number} maxLength={100} onChange={(e) => setNumber(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
        </Field>
      </div>
    </Modal>
  )
}

/** "Copy Formulation {name} - {number}" — New Name + New Number; the copy stays in the same group. */
export function CopyFormulaModal({ formula, onClose, onCopied }: {
  formula: { id: number; name: string; number?: string | null } | null
  onClose: () => void
  onCopied?: (id: number) => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [number, setNumber] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (formula) {
      setName(formula.name)
      setNumber('')
      setError(null)
    }
  }, [formula])

  const copy = useMutation({
    mutationFn: () => api.post<{ message: string; id: number }>(`/formulas/${formula!.id}/copy`, { newName: name.trim(), newNumber: number.trim() }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formulas'] })
      onCopied?.(res.data.id)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    const errs = [!name.trim() && 'New Name is required.', !number.trim() && 'New Number is required.'].filter(Boolean)
    if (errs.length) return setError(errs.join('\n'))
    copy.mutate()
  }

  return (
    <Modal
      open={!!formula}
      onClose={onClose}
      title={`Copy Formulation ${formula?.name ?? ''}${formula?.number ? ` - ${formula.number}` : ''}`}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={copy.isPending}>
            {copy.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <div className="space-y-3">
        <Field label="New Name" required>
          <input className="input" value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
        </Field>
        <Field label="New Number" required hint={formula?.number ? `Current number: ${formula.number} — numbers must be unique in the group.` : 'Numbers must be unique in the group.'}>
          <input className="input" value={number} maxLength={100} onChange={(e) => setNumber(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
        </Field>
      </div>
      <p className="mt-2 text-xs text-muted-foreground">Ingredients, colour readings, pricing settings and linked documents are copied.</p>
    </Modal>
  )
}

/** "Bulk Copying {n} Items" → "Bulk Copy Results". */
export function BulkCopyModal({ open, ids, onClose, onDone }: { open: boolean; ids: number[]; onClose: () => void; onDone: () => void }) {
  const { groups } = useGroup()
  const qc = useQueryClient()
  const [dest, setDest] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setDest(null)
      setError(null)
      setResult(null)
    }
  }, [open])

  const copy = useMutation({
    mutationFn: () => api.post<{ message: string }>('/formulas/bulk-copy', { ids, destinationGroupId: dest }),
    onSuccess: (res) => {
      setResult(res.data.message)
      qc.invalidateQueries({ queryKey: ['formulas'] })
      onDone()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  if (result)
    return (
      <Modal open={open} onClose={onClose} title="Bulk Copy Results" size="sm" footer={<button className="btn-primary" onClick={onClose}>OK</button>}>
        <ul className="space-y-1.5 text-sm">
          {result.split('\n').map((line, i) => (
            <li key={i} className="flex items-start gap-2">
              <CheckCircle2 className={clsx('mt-0.5 h-4 w-4 shrink-0', i === 0 ? 'text-success' : 'text-amber-600')} />
              <span>{line}</span>
            </li>
          ))}
        </ul>
      </Modal>
    )

  return (
    <Modal
      open={open}
      onClose={() => !copy.isPending && onClose()}
      title={`Bulk Copying ${ids.length} Item${ids.length === 1 ? '' : 's'}`}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>
            Cancel
          </button>
          <button className="btn-primary" disabled={!dest || copy.isPending || ids.length === 0} onClick={() => copy.mutate()}>
            {copy.isPending ? (
              <>
                <Spinner /> Copying . . .
              </>
            ) : (
              'Copy'
            )}
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <Field label="Select the Destination Group" required hint="Formulas whose number already exists in the destination group are skipped. Categories and materials are matched by name (and created when missing).">
        <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={dest} onChange={setDest} placeholder="Select group" />
      </Field>
    </Modal>
  )
}

/** Legacy "Print Denied" for Incomplete formulas. */
export function PrintDeniedModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title="Print Denied" size="sm" footer={<button className="btn-primary" onClick={onClose}>Ok</button>}>
      <p className="text-sm">Formula can&apos;t be printed because it has &quot;incomplete&quot; status.</p>
    </Modal>
  )
}
