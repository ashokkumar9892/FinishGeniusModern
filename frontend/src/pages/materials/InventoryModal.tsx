import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, History, Minus, Plus } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { dateTime, num } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { isBelowMin, parseNum, unitLabel, useLocations, type InventoryEntry, type Material } from './shared'

/** "+/- Adjust" on the materials grid: add stock or record consumption, plus recent movements. */
export function InventoryModal({ open, onClose, material }: { open: boolean; onClose: () => void; material: Material | null }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [mode, setMode] = useState<'add' | 'consume'>('add')
  const [qty, setQty] = useState('')
  const [locationId, setLocationId] = useState<number | ''>('')
  const [batch, setBatch] = useState('')
  const [customer, setCustomer] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const id = material?.id ?? 0
  useEffect(() => {
    if (open) {
      setMode('add')
      setQty('')
      setLocationId('')
      setBatch('')
      setCustomer('')
      setReason('')
      setError(null)
    }
  }, [open, id])

  const history = useQuery({
    queryKey: ['inventory', id],
    queryFn: () => api.get<{ onHand: number; items: InventoryEntry[] }>('/inventory', { params: { materialId: id } }).then((r) => r.data),
    enabled: open && id > 0,
  })
  const locations = useLocations(material?.groupId ?? 0, material?.materialType, open)

  const adjust = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.post<{ message: string; onHand: number }>('/inventory/adjust', body).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setQty('')
      setBatch('')
      setReason('')
      setError(null)
      qc.invalidateQueries({ queryKey: ['inventory', id] })
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['env-report'] })
      qc.invalidateQueries({ queryKey: ['history', 'Material', id] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  if (!material) return null
  const unit = unitLabel(material.materialType)
  const onHand = history.data?.onHand ?? material.onHand
  const below = isBelowMin({ minQuantity: material.minQuantity, onHand })

  const submit = () => {
    const n = parseNum(qty)
    if (n === null || n <= 0) {
      setError('Enter a quantity greater than 0.')
      return
    }
    if (mode === 'consume' && n > onHand) {
      setError(`Cannot consume ${num(n)} ${unit}: only ${num(onHand)} ${unit} on hand.`)
      return
    }
    adjust.mutate({
      materialId: material.id,
      quantity: mode === 'add' ? n : -n,
      locationId: locationId || null,
      batchNumber: batch.trim() || null,
      reason: reason.trim() || null,
      customerName: customer.trim() || null,
    })
  }

  const columns: Column<InventoryEntry>[] = [
    { key: 'createdAt', header: 'Date', cell: (r) => <span className="whitespace-nowrap">{dateTime(r.createdAt)}</span> },
    {
      key: 'quantity',
      header: 'Qty',
      align: 'right',
      cell: (r) => (
        <span className={clsx('font-medium tabular-nums', r.quantity < 0 ? 'text-destructive' : 'text-success')}>
          {r.quantity > 0 ? '+' : ''}
          {num(r.quantity)}
        </span>
      ),
    },
    { key: 'locationName', header: 'Location', hideBelow: 'sm' },
    { key: 'batchNumber', header: 'Batch', hideBelow: 'md' },
    { key: 'customerName', header: 'Customer', hideBelow: 'md' },
    { key: 'reason', header: 'Reason', hideBelow: 'sm' },
    { key: 'userName', header: 'User', hideBelow: 'lg' },
  ]

  return (
    <Modal open={open} onClose={onClose} title={`Inventory — ${material.productName}`} size="xl" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
      <div className="grid gap-5 lg:grid-cols-[320px_1fr]">
        <div className="space-y-4">
          <div className={clsx('rounded-lg border p-4', below ? 'border-amber-400 bg-amber-500/10' : 'bg-muted/40')}>
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total inventory on hand</div>
            <div className="mt-1 text-3xl font-semibold tabular-nums">
              {num(onHand)} <span className="text-base font-normal text-muted-foreground">{unit}</span>
            </div>
            <div className="mt-1 text-xs text-muted-foreground">
              Min. quantity {num(material.minQuantity)} {unit}
              {material.productCode && <> · #{material.productCode}</>}
            </div>
            {below && (
              <div className="mt-2 flex items-center gap-1.5 text-xs font-medium text-amber-700 dark:text-amber-400">
                <AlertTriangle className="h-3.5 w-3.5" /> Below minimum — reorder {num(material.minQuantity - onHand)} {unit}
              </div>
            )}
          </div>

          <ErrorBanner message={error} />
          <div className="grid grid-cols-2 gap-1 rounded-md bg-muted p-1">
            <button type="button" className={clsx('btn h-8', mode === 'add' ? 'bg-card shadow-sm text-success' : 'text-muted-foreground')} onClick={() => setMode('add')}>
              <Plus className="h-4 w-4" /> Add stock
            </button>
            <button type="button" className={clsx('btn h-8', mode === 'consume' ? 'bg-card shadow-sm text-destructive' : 'text-muted-foreground')} onClick={() => setMode('consume')}>
              <Minus className="h-4 w-4" /> Consume
            </button>
          </div>
          <Field label={`Quantity (${unit})`} required>
            <input
              className="input"
              inputMode="decimal"
              autoFocus
              value={qty}
              placeholder="0.00"
              onChange={(e) => setQty(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && submit()}
            />
          </Field>
          <Field label="Location" hint={locations.data?.length === 0 ? 'Add storage locations with “Locs”.' : undefined}>
            <select className="input" value={locationId} onChange={(e) => setLocationId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">No location</option>
              {(locations.data ?? []).map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Batch #">
              <input className="input" value={batch} onChange={(e) => setBatch(e.target.value)} maxLength={400} />
            </Field>
            <Field label="Customer Name" hint={mode === 'consume' ? 'Shown on the environmental report.' : undefined}>
              <input className="input" value={customer} onChange={(e) => setCustomer(e.target.value)} maxLength={400} />
            </Field>
          </div>
          <Field label="Reason">
            <input className="input" value={reason} placeholder={mode === 'add' ? 'e.g. Received PO 1042' : 'e.g. Used on job 2231'} onChange={(e) => setReason(e.target.value)} maxLength={400} />
          </Field>
          <button className={clsx('w-full', mode === 'add' ? 'btn-success' : 'btn-danger')} onClick={submit} disabled={adjust.isPending}>
            {adjust.isPending && <Spinner />}
            {mode === 'add' ? 'Add to inventory' : 'Record consumption'}
          </button>
        </div>

        <div className="min-w-0">
          <div className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <History className="h-4 w-4 text-muted-foreground" /> Recent movements
          </div>
          <DataTable
            bare
            dense
            pageSize={10}
            rows={history.data?.items ?? []}
            rowKey={(r) => r.id}
            columns={columns}
            loading={history.isLoading}
            emptyTitle="No inventory movements yet"
            emptyDescription="Adjustments you record here appear in this list."
          />
        </div>
      </div>
    </Modal>
  )
}
