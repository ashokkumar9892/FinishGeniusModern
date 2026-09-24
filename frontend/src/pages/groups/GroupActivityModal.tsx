import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { FileSpreadsheet, History } from 'lucide-react'
import { api, download, errorMessage } from '@/lib/api'
import { dateTime } from '@/lib/format'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { useToast } from '@/components/toast'

interface Entry {
  id: number
  entityType: string
  entityId: number
  action: string
  details?: string | null
  oldValue?: string | null
  newValue?: string | null
  userName?: string | null
  createdAt: string
}

/** The date a month back, as the date inputs want it. */
function monthAgo() {
  const d = new Date()
  d.setMonth(d.getMonth() - 1)
  return d.toISOString().slice(0, 10)
}

/**
 * Everything that happened in a group over a period, whatever it happened to — the old site's "Group Download",
 * which existed only as a spreadsheet. Here it is readable on screen first and still downloads as one.
 */
export function GroupActivityModal({ group, onClose }: { group: { id: number; name: string } | null; onClose: () => void }) {
  const toast = useToast()
  const [from, setFrom] = useState(monthAgo)
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [busy, setBusy] = useState(false)

  const q = useQuery({
    queryKey: ['group-history', group?.id, from, to],
    queryFn: () => api.get<Entry[]>('/history/group', { params: { groupId: group!.id, from, to } }).then((r) => r.data),
    enabled: !!group,
  })

  const columns: Column<Entry>[] = [
    { key: 'createdAt', header: 'When', cell: (r) => dateTime(r.createdAt), sortValue: (r) => r.createdAt },
    { key: 'userName', header: 'User', cell: (r) => r.userName || '—' },
    { key: 'entityType', header: 'Screen', cell: (r) => `${r.entityType}${r.entityId ? ` #${r.entityId}` : ''}` },
    { key: 'action', header: 'Action' },
    { key: 'details', header: 'Details', cell: (r) => <span className="whitespace-pre-wrap">{r.details || '—'}</span> },
    {
      key: 'change',
      header: 'Change',
      hideBelow: 'xl',
      cell: (r) => (r.oldValue || r.newValue ? `${r.oldValue ?? '—'} → ${r.newValue ?? '—'}` : '—'),
    },
  ]

  async function toExcel() {
    if (!group) return
    setBusy(true)
    try {
      await download('/history/group/excel', `GroupHistory-${group.name}.xlsx`, { groupId: group.id, from, to })
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!group}
      onClose={onClose}
      size="xl"
      title={`Group activity — ${group?.name ?? ''}`}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>Close</button>
          <button className="btn-primary" disabled={busy || !q.data?.length} onClick={() => void toExcel()}>
            <FileSpreadsheet className="h-4 w-4" /> Export to Excel
          </button>
        </>
      }
    >
      <div className="space-y-3">
        <ErrorBanner message={q.isError ? errorMessage(q.error) : null} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="From">
            <input className="input" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label="To">
            <input className="input" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </Field>
        </div>

        {q.isLoading ? (
          <LoadingBlock />
        ) : !q.data?.length ? (
          <EmptyState title="Nothing was recorded in this period" icon={<History className="h-5 w-5" />} />
        ) : (
          <>
            <DataTable
              rows={q.data}
              columns={columns}
              rowKey={(r) => r.id}
              bare
              dense
              searchPlaceholder="Search this period…"
              searchText={(r) => [r.userName, r.entityType, r.action, r.details].filter(Boolean).join(' ')}
            />
            {q.data.length >= 1000 && (
              <Note>
                Only the newest 1,000 entries are shown. The Excel export covers the whole period — or narrow the dates.
              </Note>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}
