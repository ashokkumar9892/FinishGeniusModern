import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { SearchSelect } from '@/components/SearchSelect'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'

/** "Blk Cpy": copies selected materials or vendors to another group, then shows "Bulk Copy Results". */
export function BulkCopyModal({ open, onClose, ids, kind, onDone }: {
  open: boolean
  onClose: () => void
  ids: number[]
  kind: 'materials' | 'vendors'
  onDone?: () => void
}) {
  const qc = useQueryClient()
  const { groups } = useGroup()
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
    mutationFn: () => api.post<{ message: string }>(`/${kind}/bulk-copy`, { ids, destinationGroupId: dest }).then((r) => r.data),
    onSuccess: (data) => {
      setResult(data.message)
      qc.invalidateQueries({ queryKey: [kind] })
      if (kind === 'materials') qc.invalidateQueries({ queryKey: ['material-categories'] })
      onDone?.()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    if (!dest) {
      setError('Please select the destination group.')
      return
    }
    setError(null)
    copy.mutate()
  }

  if (result)
    return (
      <Modal open={open} onClose={onClose} title="Bulk Copy Results" size="sm" footer={<button className="btn-primary" onClick={onClose}>OK</button>}>
        <div className="flex items-start gap-3">
          <CheckCircle2 className="h-5 w-5 text-success shrink-0 mt-0.5" />
          <p className="text-sm">{result}</p>
        </div>
      </Modal>
    )

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Bulk Copying ${ids.length} Items`}
      size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={copy.isPending || ids.length === 0}>
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
      <Field label="Select the Destination Group" required>
        <SearchSelect
          options={groups.map((g) => ({ value: g.id, label: g.name }))}
          value={dest}
          onChange={setDest}
          placeholder="Select group"
          invalid={!!error && !dest}
        />
      </Field>
      <p className="mt-3 text-xs text-muted-foreground">
        {ids.length} {kind === 'materials' ? 'material' : 'vendor'}
        {ids.length === 1 ? '' : 's'} will be copied.{' '}
        {kind === 'materials' && 'Categories and characteristics are matched by name in the destination group (created when missing).'}
      </p>
    </Modal>
  )
}
