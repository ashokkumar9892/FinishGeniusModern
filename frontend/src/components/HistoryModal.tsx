import { useQuery } from '@tanstack/react-query'
import { History } from 'lucide-react'
import { api } from '@/lib/api'
import { dateTime } from '@/lib/format'
import { EmptyState, LoadingBlock, Modal } from './ui'

interface Entry {
  id: number
  action: string
  details?: string | null
  userName?: string | null
  createdAt: string
}

/** Audit trail for one record (backend: GET /api/history?entityType=&entityId=). */
export function HistoryModal({ open, onClose, entityType, entityId, title }: {
  open: boolean
  onClose: () => void
  entityType: string
  entityId: number
  title?: string
}) {
  const q = useQuery({
    queryKey: ['history', entityType, entityId],
    queryFn: () => api.get<Entry[]>('/history', { params: { entityType, entityId } }).then((r) => r.data),
    enabled: open && entityId > 0,
  })
  return (
    <Modal open={open} onClose={onClose} title={title ?? 'History'} size="lg" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      {q.isLoading ? (
        <LoadingBlock />
      ) : !q.data?.length ? (
        <EmptyState title="No history recorded yet" icon={<History className="h-5 w-5" />} />
      ) : (
        <ol className="relative border-l ml-2 space-y-4">
          {q.data.map((e) => (
            <li key={e.id} className="ml-4">
              <span className="absolute -left-1.5 mt-1.5 h-3 w-3 rounded-full bg-primary" />
              <div className="text-sm font-medium">{e.action}</div>
              <div className="text-xs text-muted-foreground">
                {dateTime(e.createdAt)} · {e.userName || 'system'}
              </div>
              {e.details && <div className="mt-1 text-sm whitespace-pre-line">{e.details}</div>}
            </li>
          ))}
        </ol>
      )}
    </Modal>
  )
}
