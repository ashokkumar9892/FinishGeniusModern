import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { Checkbox, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import type { CopyPreview, GroupRow } from './types'

/** Process schedules reference process steps, so copying schedules always copies the steps too. */
const REQUIRES: Record<string, string> = { processSteps: 'processSchedules' }

export function CopyGroupModal({ group, onClose }: { group: GroupRow; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { refresh } = useAuth()
  const [checked, setChecked] = useState<Record<string, boolean>>({})
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitted, setSubmitted] = useState(false)

  const preview = useQuery({
    queryKey: ['group-copy-preview', group.id],
    queryFn: () => api.get<CopyPreview>(`/groups/${group.id}/copy-preview`).then((r) => r.data),
    staleTime: 0,
  })

  useEffect(() => {
    if (preview.data) setName((n) => n || preview.data.suggestedName)
  }, [preview.data])

  const forcedBy = (key: string) => {
    const parent = REQUIRES[key]
    return parent && (checked[parent] ?? true) ? parent : null
  }
  const isChecked = (key: string) => !!forcedBy(key) || (checked[key] ?? true)

  const copy = useMutation({
    mutationFn: () => {
      const options = Object.fromEntries((preview.data?.items ?? []).map((i) => [i.key, isChecked(i.key)]))
      return api.post<{ message: string; id: number }>(`/groups/${group.id}/copy`, { newName: name.trim(), options })
    },
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['groups'] })
      qc.invalidateQueries({ queryKey: ['lookups'] })
      await refresh()
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    if (!name.trim()) {
      setError('New Group Name is required.')
      return
    }
    copy.mutate()
  }

  const items = preview.data?.items ?? []
  const selectedTotal = items.filter((i) => isChecked(i.key)).reduce((s, i) => s + i.count, 0)
  const allOn = items.length > 0 && items.every((i) => isChecked(i.key))

  return (
    <Modal
      open
      onClose={() => !copy.isPending && onClose()}
      title={`Copy ${group.name} Group`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={copy.isPending}>Cancel</button>
          <button className="btn-primary" type="submit" form="copy-group-form" disabled={copy.isPending || !preview.data}>
            {copy.isPending ? <><Spinner /> Copying…</> : <><Copy className="h-4 w-4" /> Copy</>}
          </button>
        </>
      }
    >
      {preview.isLoading ? (
        <LoadingBlock label="Counting group data…" />
      ) : preview.isError ? (
        <ErrorBanner message={errorMessage(preview.error)} />
      ) : (
        <form id="copy-group-form" onSubmit={submit} noValidate className="space-y-4">
          <ErrorBanner message={error} />
          <div>
            <h4 className="font-semibold">Copy Statistics</h4>
            <p className="text-sm text-muted-foreground">Copying this group will copy the following items:</p>
          </div>
          <div className="rounded-md border divide-y">
            <div className="flex items-center justify-between px-3 py-2 bg-muted/40">
              <Checkbox
                checked={allOn}
                onChange={(v) => setChecked(Object.fromEntries(items.map((i) => [i.key, v])))}
                label={<span className="font-medium">Select all</span>}
                disabled={copy.isPending}
              />
              <span className="text-xs text-muted-foreground">{selectedTotal.toLocaleString()} items selected</span>
            </div>
            {items.map((i) => {
              const forced = forcedBy(i.key)
              return (
                <div key={i.key} className="flex items-center justify-between gap-3 px-3 py-2">
                  <div className="min-w-0">
                    <Checkbox
                      checked={isChecked(i.key)}
                      onChange={(v) => setChecked((c) => ({ ...c, [i.key]: v }))}
                      disabled={copy.isPending || !!forced}
                      label={i.label}
                    />
                    {forced && <div className="ml-6 text-xs text-muted-foreground">Required by Process Schedules</div>}
                  </div>
                  <span className="badge bg-muted text-muted-foreground tabular-nums">{i.count.toLocaleString()}</span>
                </div>
              )
            })}
          </div>
          <p className="text-xs text-muted-foreground">
            Not copied: dashboards and devices, started My Work executions, users and environmental data.
          </p>
          <Field label="New Group Name" required error={submitted && !name.trim() ? 'New Group Name is required.' : null}>
            <input
              className={submitted && !name.trim() ? 'input input-invalid' : 'input'}
              value={name}
              maxLength={200}
              disabled={copy.isPending}
              onChange={(e) => setName(e.target.value)}
            />
          </Field>
        </form>
      )}
    </Modal>
  )
}
