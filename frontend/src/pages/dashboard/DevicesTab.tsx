import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { formatDistanceToNow } from 'date-fns'
import { LineChart, Palette, Pencil, Plus, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTimeSeconds } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { ConfirmDialog, ErrorBanner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { CanisterModal, DeviceFormModal, DeviceTypes, GraphsModal, parseUtc, type DeviceRow } from './devices'

/** Dashboard › Devices. */
export function DevicesTab() {
  const { groupId } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState<{ id: number | null } | null>(null)
  const [canisters, setCanisters] = useState<number | null>(null)
  const [graphs, setGraphs] = useState<DeviceRow | null>(null)
  const [deleting, setDeleting] = useState<DeviceRow | null>(null)

  const q = useQuery({
    queryKey: ['devices', groupId, 'all'],
    queryFn: () => api.get<DeviceRow[]>('/devices', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
    refetchInterval: 60_000, // keep online/offline fresh
  })

  const remove = useMutation({
    mutationFn: (d: DeviceRow) => api.delete<{ message: string }>(`/devices/${d.id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['devices'] })
      qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const columns: Column<DeviceRow>[] = [
    { key: 'id', header: '#', className: 'w-14' },
    { key: 'groupName', header: 'Group', hideBelow: 'lg' },
    {
      key: 'name',
      header: 'Device',
      cell: (d) => (
        <div className="min-w-0">
          <div className="font-medium">{d.name}</div>
          <div className="text-xs text-muted-foreground">
            {[d.ipAddress, d.networkBridgeName && `via ${d.networkBridgeName}`].filter(Boolean).join(' · ')}
          </div>
        </div>
      ),
    },
    { key: 'deviceTypeLabel', header: 'Type', hideBelow: 'sm' },
    { key: 'description', header: 'Description', hideBelow: 'lg', cell: (d) => <span className="line-clamp-2 text-muted-foreground">{d.description}</span> },
    {
      key: 'online',
      header: 'Status',
      sortValue: (d) => (d.online ? 1 : 0),
      cell: (d) => (
        <div className="whitespace-nowrap" title={d.lastSeenAt ? `Last seen ${dateTimeSeconds(d.lastSeenAt)}` : 'Never reported'}>
          <span className="inline-flex items-center gap-1.5 text-sm">
            <span className={clsx('h-2.5 w-2.5 rounded-full', d.online ? 'bg-success ring-4 ring-success/15' : 'bg-slate-400')} />
            {d.online ? 'Online' : 'Offline'}
          </span>
          <div className="text-xs text-muted-foreground">
            {d.lastSeenAt ? `seen ${formatDistanceToNow(parseUtc(d.lastSeenAt), { addSuffix: true })}` : 'never seen'}
          </div>
        </div>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (d) => (
        <div className="flex justify-end gap-0.5">
          <button className="btn-icon" title="Graphs" onClick={() => setGraphs(d)}>
            <LineChart className="h-4 w-4" />
          </button>
          {d.deviceType === DeviceTypes.DispenseMachine && (
            <button className="btn-icon" title="Canister Tint Assignment" onClick={() => setCanisters(d.id)}>
              <Palette className="h-4 w-4" />
            </button>
          )}
          <button className="btn-icon" title="Edit" onClick={() => setForm({ id: d.id })}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(d)}>
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <>
      {q.isError && <ErrorBanner message={errorMessage(q.error)} />}
      <DataTable
        rows={q.data ?? []}
        columns={columns}
        rowKey={(d) => d.id}
        loading={q.isLoading}
        initialSort={{ key: 'id', dir: 'desc' }}
        searchPlaceholder="Search devices…"
        emptyTitle="No devices yet"
        emptyDescription="Add scales, sensors, label printers, network bridges and dispense machines."
        toolbarRight={
          <button className="btn-primary" onClick={() => setForm({ id: null })}>
            <Plus className="h-4 w-4" /> Add Device
          </button>
        }
      />

      {form && (
        <DeviceFormModal
          key={form.id ?? 'new'}
          deviceId={form.id}
          onClose={() => setForm(null)}
          onOpenCanisters={(id) => {
            setForm(null)
            setCanisters(id)
          }}
          onSaved={(res, created) => {
            setForm(null)
            if (created && res.deviceType === DeviceTypes.DispenseMachine) setCanisters(res.id)
          }}
        />
      )}
      {canisters != null && <CanisterModal deviceId={canisters} onClose={() => setCanisters(null)} />}
      {graphs && <GraphsModal device={graphs} onClose={() => setGraphs(null)} />}

      <ConfirmDialog
        open={!!deleting}
        busy={remove.isPending}
        onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
        message={deleting && `Are you sure you want to delete the "${deleting.name}" device?`}
      />
    </>
  )
}
