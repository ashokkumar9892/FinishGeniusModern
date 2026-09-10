import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { format } from 'date-fns'
import { AlertTriangle, Eye, ShoppingCart, Trash2, ArrowDownToLine } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { money, num } from '@/lib/format'
import { MaterialType } from '@/lib/types'
import { DataTable, type Column } from '@/components/DataTable'
import { SearchSelect } from '@/components/SearchSelect'
import { Card, Checkbox, EmptyState, ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { PurchaseOrderPreviewModal, type PoDocData } from './PurchaseOrderDocument'
import { defaultQuantityType, isBelowMin, jobToOrderQty, parseNum, quantityTypes, useVendors, type Material, type MessageResponse } from './shared'

interface Draft {
  qty: string
  type: string
}

interface OrderLine {
  material: Material
  qty: string
  type: string
}

const round2 = (n: number) => Math.round(n * 100) / 100

export function ReorderTab({ groupId, materials, loading, onPlaced }: {
  groupId: number
  materials: Material[]
  loading: boolean
  onPlaced: () => void
}) {
  const toast = useToast()
  const [drafts, setDrafts] = useState<Record<number, Draft>>({})
  const [selected, setSelected] = useState<number[]>([])
  const [order, setOrder] = useState<OrderLine[]>([])
  const [belowOnly, setBelowOnly] = useState(false)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    setDrafts({})
    setSelected([])
    setOrder([])
  }, [groupId])

  const draftFor = (m: Material): Draft =>
    drafts[m.id] ?? { qty: jobToOrderQty(m) > 0 ? String(round2(jobToOrderQty(m))) : '', type: defaultQuantityType(m.materialType) }
  const setDraft = (m: Material, patch: Partial<Draft>) => setDrafts((d) => ({ ...d, [m.id]: { ...draftFor(m), ...patch } }))

  const available = useMemo(
    () => materials.filter((m) => m.materialType !== MaterialType.Formula && !order.some((o) => o.material.id === m.id) && (!belowOnly || isBelowMin(m))),
    [materials, order, belowOnly],
  )
  const belowCount = materials.filter(isBelowMin).length

  const addToOrder = () => {
    const picked = materials.filter((m) => selected.includes(m.id))
    const missing = picked.filter((m) => {
      const n = parseNum(draftFor(m).qty)
      return n === null || n <= 0
    })
    if (missing.length) {
      toast.error(`Enter a PO Qty greater than 0 for: ${missing.map((m) => m.productName).join(', ')}`)
      return
    }
    setOrder((o) => [...o, ...picked.map((m) => ({ material: m, qty: draftFor(m).qty, type: draftFor(m).type }))])
    setSelected([])
  }

  const orderTotal = order.reduce((s, l) => s + (parseNum(l.qty) ?? 0) * l.material.price, 0)

  const availableColumns: Column<Material>[] = [
    { key: 'categoryName', header: 'Material Category', hideBelow: 'md' },
    { key: 'productName', header: 'Product Name', cell: (m) => <span className="font-medium">{m.productName}</span> },
    { key: 'productCode', header: 'Product #', hideBelow: 'lg' },
    { key: 'density', header: 'lb/Gal', align: 'right', hideBelow: 'xl', cell: (m) => num(m.density, 4) },
    { key: 'price', header: '$/Unit', align: 'right', hideBelow: 'sm', cell: (m) => money(m.price) },
    { key: 'materialTypeLabel', header: 'Material Type', hideBelow: 'lg' },
    {
      key: 'onHand',
      header: 'Total Inventory On Hand',
      align: 'right',
      cell: (m) => (
        <span className={clsx('tabular-nums', isBelowMin(m) && 'font-semibold text-destructive')}>
          {isBelowMin(m) && <AlertTriangle className="inline h-3.5 w-3.5 mr-1 -mt-0.5" />}
          {num(m.onHand)}
        </span>
      ),
    },
    { key: 'minQuantity', header: 'Min. Quantity', align: 'right', hideBelow: 'md', cell: (m) => num(m.minQuantity) },
    { key: 'jobToOrder', header: 'Job to Order Qty', align: 'right', sortValue: (m) => jobToOrderQty(m), cell: (m) => num(jobToOrderQty(m)) },
    {
      key: 'poQty',
      header: 'PO Qty',
      sortable: false,
      cell: (m) => (
        <input className="input h-8 w-24 text-right" inputMode="decimal" value={draftFor(m).qty} placeholder="0" onChange={(e) => setDraft(m, { qty: e.target.value })} aria-label={`PO Qty for ${m.productName}`} />
      ),
    },
    {
      key: 'poType',
      header: 'PO Qty Type',
      sortable: false,
      cell: (m) => (
        <select className="input h-8 w-32" value={draftFor(m).type} onChange={(e) => setDraft(m, { type: e.target.value })} aria-label={`PO Qty Type for ${m.productName}`}>
          {quantityTypes.map((t) => (
            <option key={t}>{t}</option>
          ))}
        </select>
      ),
    },
  ]

  return (
    <div className="space-y-5">
      <DataTable
        rows={available}
        columns={availableColumns}
        rowKey={(m) => m.id}
        loading={loading}
        selectable
        selected={selected}
        onSelectedChange={(k) => setSelected(k.map(Number))}
        rowClassName={(m) => (isBelowMin(m) && !selected.includes(m.id) ? 'bg-amber-500/10' : undefined)}
        searchPlaceholder="Search materials…"
        initialSort={{ key: 'jobToOrder', dir: 'desc' }}
        emptyTitle={belowOnly ? 'No materials below minimum' : 'No materials available'}
        toolbar={
          <div className="flex flex-wrap items-center gap-3">
            <h3 className="font-semibold">Materials available:</h3>
            <Checkbox checked={belowOnly} onChange={setBelowOnly} label={`Only below minimum (${belowCount})`} />
          </div>
        }
        toolbarRight={
          <button className="btn-primary" disabled={selected.length === 0} onClick={addToOrder}>
            <ArrowDownToLine className="h-4 w-4" /> Add to Order{selected.length > 0 && ` (${selected.length})`}
          </button>
        }
      />

      <Card
        title="Materials added to order:"
        bodyClassName="p-0"
        actions={
          <button className="btn-success" disabled={order.length === 0} onClick={() => setCreating(true)}>
            <ShoppingCart className="h-4 w-4" /> Place Order
          </button>
        }
      >
        {order.length === 0 ? (
          <EmptyState title="No materials added yet" description='Select rows above, enter the PO Qty and click "Add to Order".' icon={<ShoppingCart className="h-5 w-5" />} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-muted/60 border-b">
                <tr>
                  <th className="th">Product Name</th>
                  <th className="th hidden md:table-cell">Product #</th>
                  <th className="th hidden lg:table-cell">Material Category</th>
                  <th className="th">PO Qty</th>
                  <th className="th">PO Qty Type</th>
                  <th className="th text-right hidden sm:table-cell">$/Unit</th>
                  <th className="th text-right">Line Total</th>
                  <th className="th" />
                </tr>
              </thead>
              <tbody>
                {order.map((l, i) => {
                  const q = parseNum(l.qty)
                  const update = (patch: Partial<OrderLine>) => setOrder((o) => o.map((x, j) => (j === i ? { ...x, ...patch } : x)))
                  return (
                    <tr key={l.material.id} className="border-b last:border-0">
                      <td className="td font-medium">{l.material.productName}</td>
                      <td className="td hidden md:table-cell">{l.material.productCode}</td>
                      <td className="td hidden lg:table-cell">{l.material.categoryName}</td>
                      <td className="td">
                        <input className={clsx('input h-8 w-24 text-right', (q === null || q <= 0) && 'input-invalid')} inputMode="decimal" value={l.qty} onChange={(e) => update({ qty: e.target.value })} />
                      </td>
                      <td className="td">
                        <select className="input h-8 w-32" value={l.type} onChange={(e) => update({ type: e.target.value })}>
                          {quantityTypes.map((t) => (
                            <option key={t}>{t}</option>
                          ))}
                        </select>
                      </td>
                      <td className="td text-right tabular-nums hidden sm:table-cell">{money(l.material.price)}</td>
                      <td className="td text-right tabular-nums">{money((q ?? 0) * l.material.price)}</td>
                      <td className="td text-right">
                        <button className="btn-icon hover:text-destructive" title="Remove from order" onClick={() => setOrder((o) => o.filter((_, j) => j !== i))}>
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
              <tfoot>
                <tr className="border-t bg-muted/30">
                  <td className="td font-semibold" colSpan={3}>
                    {order.length} item{order.length === 1 ? '' : 's'}
                  </td>
                  <td className="td" colSpan={3} />
                  <td className="td text-right font-bold tabular-nums">{money(orderTotal)}</td>
                  <td className="td" />
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </Card>

      <CreateOrderModal
        open={creating}
        onClose={() => setCreating(false)}
        groupId={groupId}
        lines={order}
        onPlaced={() => {
          setCreating(false)
          setOrder([])
          setDrafts({})
          onPlaced()
        }}
      />
    </div>
  )
}

function CreateOrderModal({ open, onClose, groupId, lines, onPlaced }: {
  open: boolean
  onClose: () => void
  groupId: number
  lines: OrderLine[]
  onPlaced: () => void
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const { group } = useGroup()
  const vendors = useVendors(groupId)
  const [vendorId, setVendorId] = useState<number | null>(null)
  const [poNumber, setPoNumber] = useState('')
  const [deliveryDate, setDeliveryDate] = useState('')
  const [ship, setShip] = useState({ name: '', address1: '', address2: '', city: '', state: '', zip: '', country: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [preview, setPreview] = useState(false)

  useEffect(() => {
    if (!open) return
    // Default vendor: the one most lines already point at.
    const counts = new Map<number, number>()
    lines.forEach((l) => l.material.vendorId && counts.set(l.material.vendorId, (counts.get(l.material.vendorId) ?? 0) + 1))
    const best = [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null
    setVendorId(best)
    setPoNumber(`PO-${format(new Date(), 'yyMMdd-HHmm')}`)
    setDeliveryDate('')
    setShip({ name: group?.name ?? '', address1: '', address2: '', city: '', state: '', zip: '', country: '' })
    setErrors({})
    setServerError(null)
    setPreview(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const vendor = vendors.data?.find((v) => v.id === vendorId) ?? null

  const place = useMutation({
    mutationFn: () =>
      api
        .post<MessageResponse>('/purchase-orders', {
          groupId,
          vendorId,
          poNumber: poNumber.trim(),
          deliveryDate: deliveryDate || null,
          shipName: ship.name,
          shipAddress1: ship.address1,
          shipAddress2: ship.address2,
          shipCity: ship.city,
          shipState: ship.state,
          shipZip: ship.zip,
          shipCountry: ship.country,
          lines: lines.map((l) => ({ materialId: l.material.id, quantity: parseNum(l.qty) ?? 0, quantityType: l.type })),
        })
        .then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['purchase-orders'] })
      onPlaced()
    },
    onError: (e) => {
      setPreview(false)
      setServerError(errorMessage(e))
    },
  })

  const validate = () => {
    const errs: Record<string, string> = {}
    if (!vendorId) errs.vendor = 'Vendor is required.'
    if (!poNumber.trim()) errs.poNumber = 'P.O. Number is required.'
    if (deliveryDate && deliveryDate < format(new Date(), 'yyyy-MM-dd')) errs.deliveryDate = 'Delivery Date cannot be in the past.'
    if (lines.some((l) => (parseNum(l.qty) ?? 0) <= 0)) errs.lines = 'Every order line needs a PO Qty greater than 0.'
    setErrors(errs)
    setServerError(Object.values(errs)[0] ?? null)
    return Object.keys(errs).length === 0
  }

  const doc: PoDocData = {
    poNumber: poNumber.trim(),
    deliveryDate: deliveryDate || null,
    groupName: group?.name,
    vendor,
    ship,
    lines: lines.map((l) => ({ key: l.material.id, productName: l.material.productName, productCode: l.material.productCode, categoryName: l.material.categoryName, quantity: parseNum(l.qty) ?? 0, quantityType: l.type, unitPrice: l.material.price })),
  }

  const shipField = (k: keyof typeof ship, label: string) => (
    <Field label={label}>
      <input className="input" value={ship[k]} onChange={(e) => setShip((s) => ({ ...s, [k]: e.target.value }))} maxLength={400} />
    </Field>
  )

  return (
    <>
      <Modal
        open={open && !preview}
        onClose={onClose}
        title="Create Order"
        size="lg"
        footer={
          <>
            <button className="btn-secondary" onClick={onClose} disabled={place.isPending}>
              Cancel
            </button>
            <button className="btn-secondary" onClick={() => validate() && setPreview(true)}>
              <Eye className="h-4 w-4" /> View Order
            </button>
            <button className="btn-primary" disabled={place.isPending} onClick={() => validate() && place.mutate()}>
              {place.isPending && <Spinner />} Place Order
            </button>
          </>
        }
      >
        <ErrorBanner message={serverError} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Vendor" required error={errors.vendor} hint={vendors.data?.length === 0 ? 'No vendors yet — add one with “Vndrs”.' : undefined}>
            <SearchSelect
              options={(vendors.data ?? []).map((v) => ({ value: v.id, label: v.vendorName, sub: v.contactName ?? undefined }))}
              value={vendorId}
              onChange={setVendorId}
              invalid={!!errors.vendor}
              placeholder={vendors.isLoading ? 'Loading…' : 'Select vendor'}
            />
          </Field>
          <Field label="P.O. Number" required error={errors.poNumber}>
            <input className={clsx('input', errors.poNumber && 'input-invalid')} value={poNumber} onChange={(e) => setPoNumber(e.target.value)} maxLength={400} />
          </Field>
          <Field label="Delivery Date" error={errors.deliveryDate}>
            <input type="date" className={clsx('input', errors.deliveryDate && 'input-invalid')} value={deliveryDate} min={format(new Date(), 'yyyy-MM-dd')} onChange={(e) => setDeliveryDate(e.target.value)} />
          </Field>
          <div className="hidden sm:block" />
          <div className="sm:col-span-2 border-t pt-3 text-sm font-semibold">Ship To</div>
          {shipField('name', 'Name')}
          {shipField('address1', 'Address1')}
          {shipField('city', 'City')}
          {shipField('address2', 'Address2')}
          {shipField('state', 'State')}
          {shipField('zip', 'Zip')}
          {shipField('country', 'Country')}
        </div>
        <div className="mt-4 rounded-md bg-muted/50 px-3 py-2 text-sm flex justify-between">
          <span>
            {lines.length} line{lines.length === 1 ? '' : 's'}
          </span>
          <span className="font-semibold">{money(doc.lines.reduce((s, l) => s + l.quantity * l.unitPrice, 0))}</span>
        </div>
      </Modal>
      <PurchaseOrderPreviewModal
        open={open && preview}
        onClose={() => setPreview(false)}
        po={doc}
        footer={
          <>
            <button className="btn-secondary" onClick={() => setPreview(false)} disabled={place.isPending}>
              Back
            </button>
            <button className="btn-primary" onClick={() => place.mutate()} disabled={place.isPending}>
              {place.isPending && <Spinner />} Place Order
            </button>
          </>
        }
      />
    </>
  )
}
