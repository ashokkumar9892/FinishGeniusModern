import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { useToast } from '@/components/toast'

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

/** "Copy Formula" — asks for the new name; the copy stays in the same group. */
export function CopyFormulaModal({ formula, onClose, onCopied }: {
  formula: { id: number; name: string } | null
  onClose: () => void
  onCopied?: (id: number) => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (formula) {
      setName(`${formula.name} - Copy`)
      setError(null)
    }
  }, [formula])

  const copy = useMutation({
    mutationFn: () => api.post<{ message: string; id: number }>(`/formulas/${formula!.id}/copy`, { newName: name.trim() }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['formulas'] })
      onCopied?.(res.data.id)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    if (!name.trim()) return setError('New Name is required.')
    copy.mutate()
  }

  return (
    <Modal
      open={!!formula}
      onClose={onClose}
      title={`Copy ${formula?.name ?? 'Formula'}`}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={copy.isPending}>
            {copy.isPending && <Spinner />} Copy
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <Field label="New Name" required>
        <input className="input" value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && submit()} />
      </Field>
      <p className="mt-2 text-xs text-muted-foreground">Ingredients, colour readings and pricing settings are copied. Documents are not.</p>
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
        <div className="flex items-start gap-2 text-sm">
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-success" />
          <span>{result}</span>
        </div>
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
          <button className="btn-primary" disabled={!dest || copy.isPending} onClick={() => copy.mutate()}>
            {copy.isPending ? (
              <>
                <Spinner /> Copying...
              </>
            ) : (
              'Copy'
            )}
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      <Field label="Select the Destination Group" required hint="Categories and materials are matched by name in the destination group (and created when missing).">
        <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={dest} onChange={setDest} placeholder="Select group" />
      </Field>
    </Modal>
  )
}
