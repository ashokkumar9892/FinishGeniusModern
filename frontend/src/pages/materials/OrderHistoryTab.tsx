import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Eye, ShoppingCart } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { ErrorBanner, LoadingBlock, Modal } from '@/components/ui'
import { PurchaseOrderPreviewModal, dayOnly, type PoDocData } from './PurchaseOrderDocument'
import type { PurchaseOrderDetail, PurchaseOrderSummary } from './shared'

export const toDocData = (p: PurchaseOrderDetail): PoDocData => ({
  poNumber: p.poNumber,
  createdAt: p.createdAt,
  deliveryDate: p.deliveryDate,
  groupName: p.groupName,
  createdByName: p.createdByName,
  vendor: p.vendor,
  ship: { name: p.shipName, address1: p.shipAddress1, address2: p.shipAddress2, city: p.shipCity, state: p.shipState, zip: p.shipZip, country: p.shipCountry },
  lines: p.lines.map((l) => ({ key: l.id, productName: l.productName, productCode: l.productCode, categoryName: l.categoryName, quantity: l.quantity, quantityType: l.quantityType, unitPrice: l.unitPrice })),
})

export function OrderHistoryTab({ groupId, onReorder }: { groupId: number; onReorder: () => void }) {
  const [viewId, setViewId] = useState<number | null>(null)
  const list = useQuery({
    queryKey: ['purchase-orders', groupId],
    queryFn: () => api.get<PurchaseOrderSummary[]>('/purchase-orders', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
  const detail = useQuery({
    queryKey: ['purchase-order', viewId],
    queryFn: () => api.get<PurchaseOrderDetail>(`/purchase-orders/${viewId}`).then((r) => r.data),
    enabled: !!viewId,
  })

  const columns: Column<PurchaseOrderSummary>[] = [
    { key: 'poNumber', header: 'PO #', cell: (p) => <span className="font-medium">{p.poNumber}</span> },
    { key: 'vendorName', header: 'Vendor' },
    { key: 'createdAt', header: 'Created', cell: (p) => <span className="whitespace-nowrap">{dateTime(p.createdAt)}</span>, hideBelow: 'sm' },
    { key: 'deliveryDate', header: 'Delivery Date', cell: (p) => dayOnly(p.deliveryDate), hideBelow: 'md' },
    { key: 'lineCount', header: 'Lines', align: 'right', hideBelow: 'md' },
    { key: 'total', header: 'Total', align: 'right', cell: (p) => <span className="tabular-nums">{money(p.total)}</span> },
    { key: 'createdByName', header: 'Created By', hideBelow: 'lg' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (p) => (
        <button className="btn-ghost btn-sm" title="View / print" onClick={() => setViewId(p.id)}>
          <Eye className="h-4 w-4" /> View
        </button>
      ),
    },
  ]

  return (
    <>
      {list.isError && <ErrorBanner message={errorMessage(list.error)} />}
      <DataTable
        rows={list.data ?? []}
        columns={columns}
        rowKey={(p) => p.id}
        loading={list.isLoading}
        searchPlaceholder="Search orders…"
        initialSort={{ key: 'createdAt', dir: 'desc' }}
        onRowClick={(p) => setViewId(p.id)}
        emptyTitle="No orders placed yet"
        emptyDescription="Orders placed from Reorder Materials appear here."
        emptyAction={
          <button className="btn-primary" onClick={onReorder}>
            <ShoppingCart className="h-4 w-4" /> Reorder Materials
          </button>
        }
      />
      {viewId && detail.isLoading && (
        <Modal open onClose={() => setViewId(null)} title="Purchase Order">
          <LoadingBlock />
        </Modal>
      )}
      {viewId && detail.isError && (
        <Modal open onClose={() => setViewId(null)} title="Purchase Order">
          <ErrorBanner message={errorMessage(detail.error)} />
        </Modal>
      )}
      <PurchaseOrderPreviewModal open={!!viewId && !!detail.data} onClose={() => setViewId(null)} po={detail.data ? toDocData(detail.data) : null} />
    </>
  )
}
