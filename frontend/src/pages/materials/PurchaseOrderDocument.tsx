import { Printer } from 'lucide-react'
import type { ReactNode } from 'react'
import { money, num, dateTime } from '@/lib/format'
import { Modal } from '@/components/ui'
import { PrintArea } from './PrintArea'

export interface PoDocData {
  poNumber: string
  createdAt?: string | null
  deliveryDate?: string | null
  groupName?: string | null
  createdByName?: string | null
  vendor: {
    vendorName: string
    address?: string | null
    city?: string | null
    state?: string | null
    zip?: string | null
    contactName?: string | null
    officePhone?: string | null
    vendorEmail?: string | null
    paymentTerms?: string | null
    accountNumber?: string | null
  } | null
  ship: {
    name?: string | null
    address1?: string | null
    address2?: string | null
    city?: string | null
    state?: string | null
    zip?: string | null
    country?: string | null
  }
  lines: {
    key: string | number
    productName: string | null
    productCode: string | null
    categoryName?: string | null
    quantity: number
    quantityType: string | null
    unitPrice: number
  }[]
}

/** "2026-09-15" or "2026-09-15T00:00:00" → 09/15/2026 without timezone shifting (delivery dates are calendar days). */
export function dayOnly(value?: string | null) {
  if (!value) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  return m ? `${m[2]}/${m[3]}/${m[1]}` : value
}

const cityLine = (city?: string | null, state?: string | null, zip?: string | null) =>
  [city, [state, zip].filter(Boolean).join(' ')].filter(Boolean).join(', ')

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground mb-1">{title}</div>
      <div className="text-sm leading-relaxed">{children}</div>
    </div>
  )
}

/** Printable purchase order (preview before placing, and Order History → View). */
export function PurchaseOrderDocument({ po }: { po: PoDocData }) {
  const total = po.lines.reduce((s, l) => s + l.quantity * l.unitPrice, 0)
  const v = po.vendor
  return (
    <div className="text-foreground">
      <div className="flex flex-wrap items-start justify-between gap-4 border-b-2 border-primary pb-3">
        <div>
          <div className="text-2xl font-bold tracking-tight">Purchase Order</div>
          {po.groupName && <div className="text-sm text-muted-foreground">{po.groupName}</div>}
        </div>
        <table className="text-sm">
          <tbody>
            <tr>
              <td className="pr-3 text-muted-foreground">P.O. Number</td>
              <td className="font-semibold">{po.poNumber || '—'}</td>
            </tr>
            <tr>
              <td className="pr-3 text-muted-foreground">Order Date</td>
              <td>{po.createdAt ? dateTime(po.createdAt) : dayOnly(new Date().toISOString())}</td>
            </tr>
            <tr>
              <td className="pr-3 text-muted-foreground">Delivery Date</td>
              <td>{dayOnly(po.deliveryDate) || '—'}</td>
            </tr>
            {po.createdByName && (
              <tr>
                <td className="pr-3 text-muted-foreground">Requested By</td>
                <td>{po.createdByName}</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="grid gap-4 py-4 sm:grid-cols-2">
        <Block title="Vendor">
          {v ? (
            <>
              <div className="font-semibold">{v.vendorName}</div>
              {v.address && <div>{v.address}</div>}
              {cityLine(v.city, v.state, v.zip) && <div>{cityLine(v.city, v.state, v.zip)}</div>}
              {v.contactName && <div>Attn: {v.contactName}</div>}
              {[v.officePhone, v.vendorEmail].filter(Boolean).length > 0 && <div>{[v.officePhone, v.vendorEmail].filter(Boolean).join(' · ')}</div>}
              {(v.paymentTerms || v.accountNumber) && (
                <div className="text-muted-foreground">{[v.paymentTerms && `Terms: ${v.paymentTerms}`, v.accountNumber && `Account #: ${v.accountNumber}`].filter(Boolean).join(' · ')}</div>
              )}
            </>
          ) : (
            <span className="text-muted-foreground">No vendor selected</span>
          )}
        </Block>
        <Block title="Ship To">
          <div className="font-semibold">{po.ship.name || '—'}</div>
          {po.ship.address1 && <div>{po.ship.address1}</div>}
          {po.ship.address2 && <div>{po.ship.address2}</div>}
          {cityLine(po.ship.city, po.ship.state, po.ship.zip) && <div>{cityLine(po.ship.city, po.ship.state, po.ship.zip)}</div>}
          {po.ship.country && <div>{po.ship.country}</div>}
        </Block>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-muted/60 border-y">
            <tr>
              <th className="th">#</th>
              <th className="th">Product</th>
              <th className="th">Product #</th>
              <th className="th text-right">Qty</th>
              <th className="th">Unit</th>
              <th className="th text-right">Unit Price</th>
              <th className="th text-right">Amount</th>
            </tr>
          </thead>
          <tbody>
            {po.lines.map((l, i) => (
              <tr key={l.key} className="border-b">
                <td className="td text-muted-foreground">{i + 1}</td>
                <td className="td">
                  <div className="font-medium">{l.productName}</div>
                  {l.categoryName && <div className="text-xs text-muted-foreground">{l.categoryName}</div>}
                </td>
                <td className="td">{l.productCode}</td>
                <td className="td text-right tabular-nums">{num(l.quantity)}</td>
                <td className="td">{l.quantityType}</td>
                <td className="td text-right tabular-nums">{money(l.unitPrice)}</td>
                <td className="td text-right tabular-nums">{money(l.quantity * l.unitPrice)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={6} className="td text-right font-semibold">
                Total
              </td>
              <td className="td text-right font-bold tabular-nums">{money(total)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      <p className="mt-4 text-xs text-muted-foreground">Unit prices are the material prices on file when the order was placed.</p>
    </div>
  )
}

/** Modal preview with a Print button; the document is also mounted into the print area while open. */
export function PurchaseOrderPreviewModal({ open, onClose, po, footer }: { open: boolean; onClose: () => void; po: PoDocData | null; footer?: ReactNode }) {
  if (!open || !po) return null
  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        title={`Purchase Order ${po.poNumber}`}
        size="lg"
        footer={
          <>
            <button className="btn-secondary mr-auto" onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print
            </button>
            {footer ?? (
              <button className="btn-primary" onClick={onClose}>
                Close
              </button>
            )}
          </>
        }
      >
        <PurchaseOrderDocument po={po} />
      </Modal>
      <PrintArea>
        <PurchaseOrderDocument po={po} />
      </PrintArea>
    </>
  )
}
