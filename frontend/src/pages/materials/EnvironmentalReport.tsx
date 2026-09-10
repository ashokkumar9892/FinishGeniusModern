import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { format } from 'date-fns'
import { FileSpreadsheet, Leaf, Printer } from 'lucide-react'
import { api, download, errorMessage } from '@/lib/api'
import { num } from '@/lib/format'
import { DataTable, type Column } from '@/components/DataTable'
import { Card, ErrorBanner, Field, Spinner, StatCard } from '@/components/ui'
import { useToast } from '@/components/toast'
import { PrintArea } from './PrintArea'
import { dayOnly } from './PurchaseOrderDocument'

interface Row {
  groupName: string
  categoryName: string | null
  productName: string
  productCode: string | null
  locationName: string | null
  customerName: string | null
  totalGallons: number
  totalVoc: number
  totalHap: number
  totalTap: number
}

interface Report {
  rows: Row[]
  totals: { totalGallons: number; totalVoc: number; totalHap: number; totalTap: number }
}

const headers = [
  'Group',
  'Category',
  'Product Name',
  'Product Number',
  'Location',
  'Customer Name',
  'Total Gallons Consumed',
  "Total VOC's (lb) Emitted",
  "Total HAP's (lb) Emitted",
  "Total TAP's (lb) Emitted",
]

/** Reports → Environmental Report: consumption (negative inventory movements) × VOC / HAP / TAP (lb/gal). */
export function EnvironmentalReport({ groupId, groupName }: { groupId: number; groupName?: string }) {
  const toast = useToast()
  const [from, setFrom] = useState(() => format(new Date(), 'yyyy') + '-01-01')
  const [to, setTo] = useState(() => format(new Date(), 'yyyy-MM-dd'))
  const [exporting, setExporting] = useState(false)
  const invalid = !!from && !!to && from > to

  const q = useQuery({
    queryKey: ['env-report', groupId, from, to],
    queryFn: () => api.get<Report>('/material-reports/environmental', { params: { groupId, from: from || undefined, to: to || undefined } }).then((r) => r.data),
    enabled: groupId > 0 && !invalid,
  })

  const excel = async () => {
    setExporting(true)
    try {
      await download('/material-reports/environmental/excel', 'EnvironmentalReport.xlsx', { groupId, from: from || undefined, to: to || undefined })
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setExporting(false)
    }
  }

  const rows = q.data?.rows ?? []
  const t = q.data?.totals
  const columns: Column<Row & { key: string }>[] = [
    { key: 'groupName', header: 'Group', hideBelow: 'xl' },
    { key: 'categoryName', header: 'Category', hideBelow: 'md' },
    { key: 'productName', header: 'Product Name', cell: (r) => <span className="font-medium">{r.productName}</span> },
    { key: 'productCode', header: 'Product Number', hideBelow: 'lg' },
    { key: 'locationName', header: 'Location', hideBelow: 'md' },
    { key: 'customerName', header: 'Customer Name', hideBelow: 'lg' },
    { key: 'totalGallons', header: 'Total Gallons Consumed', align: 'right', cell: (r) => num(r.totalGallons) },
    { key: 'totalVoc', header: "Total VOC's (lb) Emitted", align: 'right', cell: (r) => num(r.totalVoc) },
    { key: 'totalHap', header: "Total HAP's (lb) Emitted", align: 'right', cell: (r) => num(r.totalHap), hideBelow: 'sm' },
    { key: 'totalTap', header: "Total TAP's (lb) Emitted", align: 'right', cell: (r) => num(r.totalTap), hideBelow: 'sm' },
  ]
  const keyed = rows.map((r, i) => ({ ...r, key: `${i}` }))
  const range = `${from ? dayOnly(from) : 'All dates'} – ${to ? dayOnly(to) : 'today'}`

  return (
    <div className="space-y-4">
      <Card
        title={
          <span className="flex items-center gap-2">
            <Leaf className="h-4 w-4 text-success" /> Environmental Report
          </span>
        }
        actions={
          <>
            <button className="btn-secondary btn-sm" onClick={() => window.print()} disabled={!rows.length}>
              <Printer className="h-4 w-4" /> Export to PDF
            </button>
            <button className="btn-secondary btn-sm" onClick={excel} disabled={exporting || invalid}>
              {exporting ? <Spinner /> : <FileSpreadsheet className="h-4 w-4" />} Export to Excel
            </button>
          </>
        }
      >
        <div className="flex flex-wrap items-end gap-3">
          <Field label="From">
            <input type="date" className="input w-44" value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label="To">
            <input type="date" className="input w-44" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
          </Field>
          <p className="text-xs text-muted-foreground pb-2 max-w-md">
            Consumption is every negative inventory adjustment in the range (recorded with “+/- Adjust”). Emissions = gallons consumed × the material’s VOC / HAP / TAP (lb per gallon).
          </p>
        </div>
        {invalid && <div className="mt-3"><ErrorBanner message="The From date must be on or before the To date." /></div>}
        {q.isError && <div className="mt-3"><ErrorBanner message={errorMessage(q.error)} /></div>}
      </Card>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard label="Gallons consumed" value={num(t?.totalGallons)} />
        <StatCard label="VOC emitted (lb)" value={num(t?.totalVoc)} tone="primary" />
        <StatCard label="HAP emitted (lb)" value={num(t?.totalHap)} />
        <StatCard label="TAP emitted (lb)" value={num(t?.totalTap)} />
      </div>

      <DataTable
        rows={keyed}
        columns={columns}
        rowKey={(r) => r.key}
        loading={q.isLoading && !invalid}
        searchPlaceholder="Search report…"
        emptyTitle="No consumption recorded in this date range"
        emptyDescription='Record consumption with the "+/- Adjust" button on a material (Consume).'
      />

      {rows.length > 0 && (
        <PrintArea>
          <h1 style={{ fontSize: 18, fontWeight: 700, marginBottom: 2 }}>Environmental Report</h1>
          <div style={{ fontSize: 12, color: '#555', marginBottom: 12 }}>
            {groupName} · {range} · printed {format(new Date(), 'MM/dd/yyyy h:mm a')}
          </div>
          <table style={{ fontSize: 11 }}>
            <thead>
              <tr>
                {headers.map((h, i) => (
                  <th key={h} style={{ textAlign: i >= 6 ? 'right' : 'left' }}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  <td>{r.groupName}</td>
                  <td>{r.categoryName}</td>
                  <td>{r.productName}</td>
                  <td>{r.productCode}</td>
                  <td>{r.locationName}</td>
                  <td>{r.customerName}</td>
                  <td style={{ textAlign: 'right' }}>{num(r.totalGallons)}</td>
                  <td style={{ textAlign: 'right' }}>{num(r.totalVoc)}</td>
                  <td style={{ textAlign: 'right' }}>{num(r.totalHap)}</td>
                  <td style={{ textAlign: 'right' }}>{num(r.totalTap)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr style={{ fontWeight: 700 }}>
                <td colSpan={6}>Total</td>
                <td style={{ textAlign: 'right' }}>{num(t?.totalGallons)}</td>
                <td style={{ textAlign: 'right' }}>{num(t?.totalVoc)}</td>
                <td style={{ textAlign: 'right' }}>{num(t?.totalHap)}</td>
                <td style={{ textAlign: 'right' }}>{num(t?.totalTap)}</td>
              </tr>
            </tfoot>
          </table>
        </PrintArea>
      )}
    </div>
  )
}
