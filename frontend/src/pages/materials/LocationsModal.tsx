import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { MapPin, Plus, Save, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { MaterialType } from '@/lib/types'
import { ConfirmDialog, EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { editableTypes, useLocations, type MaterialLocation, type MessageResponse } from './shared'

interface Row {
  key: string
  id: number | null
  name: string
  original: string
}

/** "Locs": storage locations per material type (used by inventory adjustments and the environmental report). */
export function LocationsModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { groupId } = useGroup()
  const [type, setType] = useState<number>(MaterialType.Base)
  const [rows, setRows] = useState<Row[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busyKey, setBusyKey] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<Row | null>(null)
  const locations = useLocations(groupId, type, open)

  useEffect(() => {
    if (!locations.data) return
    setRows((prev) => {
      const drafts = prev.filter((r) => r.id === null)
      return [...locations.data.map((l: MaterialLocation) => ({ key: `l${l.id}`, id: l.id, name: l.name, original: l.name })), ...drafts]
    })
  }, [locations.data])

  useEffect(() => {
    setRows([])
    setError(null)
  }, [type, open])

  const refresh = () => qc.invalidateQueries({ queryKey: ['material-locations'] })

  const save = useMutation({
    mutationFn: (r: Row) =>
      (r.id
        ? api.put<MessageResponse>(`/material-locations/${r.id}`, { groupId, materialType: type, name: r.name })
        : api.post<MessageResponse>('/material-locations', { groupId, materialType: type, name: r.name })
      ).then((res) => res.data),
    onMutate: (r) => setBusyKey(r.key),
    onSuccess: (res, r) => {
      toast.success(res.message)
      setError(null)
      if (!r.id) setRows((all) => all.filter((x) => x.key !== r.key))
      refresh()
    },
    onError: (e) => setError(errorMessage(e)),
    onSettled: () => setBusyKey(null),
  })

  const remove = useMutation({
    mutationFn: (r: Row) => api.delete<MessageResponse>(`/material-locations/${r.id}`).then((res) => res.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      refresh()
    },
    onError: (e) => {
      setDeleting(null)
      setError(errorMessage(e))
    },
  })

  const update = (key: string, name: string) => setRows((all) => all.map((r) => (r.key === key ? { ...r, name } : r)))
  const addRow = () => setRows((all) => [...all, { key: `n${Date.now()}`, id: null, name: '', original: '' }])

  const trySave = (r: Row) => {
    if (!r.name.trim()) {
      setError('Location name is required.')
      return
    }
    save.mutate({ ...r, name: r.name.trim() })
  }

  return (
    <Modal open={open} onClose={onClose} title="Storage Locations" size="md" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      <Field label="Select Material Type">
        <select className="input" value={type} onChange={(e) => setType(Number(e.target.value))}>
          {editableTypes.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </select>
      </Field>
      <div className="mt-4">
        <ErrorBanner message={error} />
        {locations.isLoading ? (
          <LoadingBlock />
        ) : rows.length === 0 ? (
          <EmptyState icon={<MapPin className="h-5 w-5" />} title="No locations for this material type" description="Add the rooms, cabinets or shelves where this stock is kept." />
        ) : (
          <ul className="space-y-2">
            {rows.map((r) => {
              const dirty = r.id === null || r.name.trim() !== r.original
              return (
                <li key={r.key} className="flex items-center gap-2">
                  <input
                    className="input"
                    value={r.name}
                    placeholder="Location name"
                    autoFocus={r.id === null}
                    onChange={(e) => update(r.key, e.target.value)}
                    onKeyDown={(e) => e.key === 'Enter' && dirty && trySave(r)}
                    maxLength={400}
                  />
                  <button className="btn-icon text-success" title="Save" disabled={!dirty || busyKey === r.key} onClick={() => trySave(r)}>
                    {busyKey === r.key ? <Spinner /> : <Save className="h-4 w-4" />}
                  </button>
                  <button
                    className="btn-icon hover:text-destructive"
                    title="Delete"
                    onClick={() => (r.id ? setDeleting(r) : setRows((all) => all.filter((x) => x.key !== r.key)))}
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </li>
              )
            })}
          </ul>
        )}
        <button className="btn-secondary btn-sm mt-3" onClick={addRow} title="Add location">
          <Plus className="h-4 w-4" /> Add location
        </button>
      </div>
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        message={`Are you sure you want to delete the "${deleting?.original}" location?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
      />
    </Modal>
  )
}
