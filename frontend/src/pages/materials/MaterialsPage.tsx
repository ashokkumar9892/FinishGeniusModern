import { useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Boxes, Copy, DollarSign, FileText, FolderOpen, History, MapPin, Package, Pencil, Plus, Trash2, Truck, Upload } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { isAdmin, useVisibleTab } from '@/lib/access'
import { money, num } from '@/lib/format'
import { MaterialType } from '@/lib/types'
import { DataTable, type Column } from '@/components/DataTable'
import { DocumentLibraryModal } from '@/components/DocumentLibraryModal'
import { EntityDocuments } from '@/components/EntityDocuments'
import { HistoryModal } from '@/components/HistoryModal'
import { ConfirmDialog, ErrorBanner, PageHeader, StatCard, Tabs } from '@/components/ui'
import { useToast } from '@/components/toast'
import { BulkCopyModal } from './BulkCopyModal'
import { BulkUploadModal } from './BulkUploadModal'
import { EnvironmentalReport } from './EnvironmentalReport'
import { InventoryModal } from './InventoryModal'
import { LocationsModal } from './LocationsModal'
import { MaterialFormModal } from './MaterialFormModal'
import { OrderHistoryTab } from './OrderHistoryTab'
import { ReorderTab } from './ReorderTab'
import { VendorSetupModal } from './VendorSetupModal'
import { VendorsTab } from './VendorsTab'
import { isBelowMin, unitLabel, useMaterials, useVendors, type Material, type MessageResponse } from './shared'

type TabKey = 'base' | 'pigment' | 'dye' | 'equipment' | 'sundry-items' | 'reorder-materials' | 'order-history' | 'vendors' | 'reports'

/** Material tabs → the material types they show (Product-type coatings are listed with Base). */
const materialTabs: Partial<Record<TabKey, { types: number[]; defaultType: number; label: string }>> = {
  base: { types: [MaterialType.Base, MaterialType.Product], defaultType: MaterialType.Base, label: 'base materials' },
  pigment: { types: [MaterialType.Pigment], defaultType: MaterialType.Pigment, label: 'pigments' },
  dye: { types: [MaterialType.Dye], defaultType: MaterialType.Dye, label: 'dyes' },
  equipment: { types: [MaterialType.Equipment], defaultType: MaterialType.Equipment, label: 'equipment' },
  'sundry-items': { types: [MaterialType.Sundry], defaultType: MaterialType.Sundry, label: 'sundry items' },
}

const tabLabels: [TabKey, string][] = [
  ['base', 'Base Materials'],
  ['pigment', 'Pigments'],
  ['dye', 'Dyes'],
  ['equipment', 'Equipment'],
  ['sundry-items', 'Sundry Items'],
  ['reorder-materials', 'Reorder Materials'],
  ['order-history', 'Order History'],
  ['vendors', 'Vendors'],
  ['reports', 'Reports'],
]

export default function MaterialsPage() {
  const me = useMe()
  const admin = isAdmin(me)
  const qc = useQueryClient()
  const toast = useToast()
  const { groupId, group } = useGroup()
  const location = useLocation()
  const navigate = useNavigate()

  const hash = location.hash.replace(/^#/, '') as TabKey
  const requestedTab: TabKey = tabLabels.some(([k]) => k === hash) ? hash : 'base'
  const { allowed: tabAllowed, tab: visibleTab } = useVisibleTab('materials', tabLabels.map(([k]) => k), requestedTab)
  const tab: TabKey = visibleTab ?? requestedTab
  const setTab = (k: TabKey) => navigate({ search: location.search, hash: k }, { replace: true })
  const mt = materialTabs[tab]

  const materials = useMaterials(groupId)
  const vendors = useVendors(groupId)
  const all = useMemo(() => materials.data ?? [], [materials.data])

  const [selMaterials, setSelMaterials] = useState<number[]>([])
  const [selVendors, setSelVendors] = useState<number[]>([])
  useEffect(() => {
    setSelMaterials([])
    setSelVendors([])
  }, [tab, groupId])

  // Modals
  const [form, setForm] = useState<{ open: boolean; material: Material | null }>({ open: false, material: null })
  const [inventoryFor, setInventoryFor] = useState<Material | null>(null)
  const [docsFor, setDocsFor] = useState<Material | null>(null)
  const [historyFor, setHistoryFor] = useState<Material | null>(null)
  const [deleting, setDeleting] = useState<Material | null>(null)
  const [bulkDeleting, setBulkDeleting] = useState(false)
  const [bulkCopy, setBulkCopy] = useState<{ kind: 'materials' | 'vendors'; ids: number[] } | null>(null)
  const [bulkUpload, setBulkUpload] = useState(false)
  const [vendorSetup, setVendorSetup] = useState<{ open: boolean; id: number | null }>({ open: false, id: null })
  const [locsOpen, setLocsOpen] = useState(false)
  const [docsOpen, setDocsOpen] = useState(false)

  const rows = useMemo(() => (mt ? all.filter((m) => mt.types.includes(m.materialType)) : []), [all, mt])
  const counts = useMemo(() => {
    const c: Partial<Record<TabKey, number>> = {}
    for (const [k, v] of Object.entries(materialTabs)) c[k as TabKey] = all.filter((m) => v!.types.includes(m.materialType)).length
    c.vendors = vendors.data?.length
    return c
  }, [all, vendors.data])

  const kpi = useMemo(() => {
    const tracked = all.filter((m) => m.materialType !== MaterialType.Formula)
    return {
      items: tracked.length,
      value: tracked.reduce((s, m) => s + Math.max(0, m.onHand) * m.price, 0),
      below: tracked.filter(isBelowMin).length,
    }
  }, [all])

  const copyOne = useMutation({
    mutationFn: (id: number) => api.post<MessageResponse>(`/materials/${id}/copy`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['materials'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const removeOne = useMutation({
    mutationFn: (id: number) => api.delete<MessageResponse>(`/materials/${id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['material-categories'] })
    },
    onError: (e) => {
      setDeleting(null)
      toast.error(errorMessage(e))
    },
  })

  const removeMany = useMutation({
    mutationFn: (ids: number[]) => api.post<MessageResponse>('/materials/bulk-delete', { ids }).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setBulkDeleting(false)
      setSelMaterials([])
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['material-categories'] })
    },
    onError: (e) => {
      setBulkDeleting(false)
      toast.error(errorMessage(e))
    },
  })

  const copyIds = tab === 'vendors' ? selVendors : mt ? selMaterials : []
  const isEquipmentTab = tab === 'equipment'

  const columns: Column<Material>[] = [
    { key: 'id', header: '#', className: 'w-14 tabular-nums text-muted-foreground' },
    { key: 'groupName', header: 'Group', hideBelow: 'xl' },
    { key: 'categoryName', header: 'Material Category', hideBelow: 'md' },
    {
      key: 'productName',
      header: 'Product Name',
      cell: (m) => (
        <div className="min-w-[10rem]">
          <div className="font-medium">{m.productName}</div>
          {m.vendorName && <div className="text-xs text-muted-foreground">{m.vendorName}</div>}
        </div>
      ),
    },
    { key: 'productCode', header: 'Product #', hideBelow: 'lg' },
    { key: 'density', header: 'lb/Gal', align: 'right', hideBelow: 'lg', cell: (m) => num(m.density, 4) },
    { key: 'price', header: '$/Unit', align: 'right', hideBelow: 'sm', cell: (m) => money(m.price) },
    {
      key: 'materialTypeLabel',
      header: 'Material Type',
      hideBelow: 'xl',
      cell: (m) => <span className={clsx('badge', m.materialType === MaterialType.Product ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground')}>{m.materialTypeLabel}</span>,
    },
    ...(isEquipmentTab
      ? []
      : ([
          { key: 'voc', header: 'VOC', align: 'right', hideBelow: 'lg', cell: (m) => num(m.voc, 4) },
          { key: 'hap', header: 'HAP', align: 'right', hideBelow: 'xl', cell: (m) => num(m.hap, 4) },
          { key: 'tap', header: 'TAP', align: 'right', hideBelow: 'xl', cell: (m) => num(m.tap, 4) },
        ] as Column<Material>[])),
    {
      key: 'onHand',
      header: 'Total Inventory On Hand',
      align: 'center',
      cell: (m) => (
        <button
          className={clsx('btn-sm btn text-white shadow-sm whitespace-nowrap', isBelowMin(m) ? 'bg-destructive hover:bg-destructive/90' : 'bg-primary hover:bg-primary/90')}
          title={isBelowMin(m) ? `Below minimum (${num(m.minQuantity)} ${unitLabel(m.materialType)})` : 'Adjust inventory'}
          onClick={() => setInventoryFor(m)}
        >
          {isBelowMin(m) && <AlertTriangle className="h-3.5 w-3.5" />}
          +/- Adjust {num(m.onHand)}
        </button>
      ),
    },
    { key: 'minQuantity', header: 'Min. Quantity', align: 'right', hideBelow: 'md', cell: (m) => num(m.minQuantity) },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (m) => (
        <div className="flex justify-end gap-0.5">
          <button className="btn-icon" title="Documents" onClick={() => setDocsFor(m)}>
            <FileText className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Edit" onClick={() => setForm({ open: true, material: m })}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon" title="Copy" disabled={copyOne.isPending} onClick={() => copyOne.mutate(m.id)}>
            <Copy className="h-4 w-4" />
          </button>
          {admin && (
            <>
              <button className="btn-icon" title="History" onClick={() => setHistoryFor(m)}>
                <History className="h-4 w-4" />
              </button>
              <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(m)}>
                <Trash2 className="h-4 w-4" />
              </button>
            </>
          )}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Equipment & Materials List"
        breadcrumbs={['Equipment & Materials List']}
        subtitle={group?.name}
        actions={
          <>
            {admin && (
              <>
                <button
                  className="btn-secondary btn-sm"
                  disabled={copyIds.length === 0}
                  title={copyIds.length ? `Copy ${copyIds.length} selected ${tab === 'vendors' ? 'vendors' : 'items'} to another group` : 'Select rows to copy'}
                  onClick={() => setBulkCopy({ kind: tab === 'vendors' ? 'vendors' : 'materials', ids: copyIds })}
                >
                  <Copy className="h-4 w-4" /> Blk Cpy
                </button>
                <button className="btn-secondary btn-sm" title="Bulk Material Excel Upload" onClick={() => setBulkUpload(true)}>
                  <Upload className="h-4 w-4" /> Blk Upld
                </button>
                <button
                  className="btn-secondary btn-sm hover:text-destructive"
                  disabled={!mt || selMaterials.length === 0}
                  title={selMaterials.length ? `Delete ${selMaterials.length} selected items` : 'Select material rows to delete'}
                  onClick={() => setBulkDeleting(true)}
                >
                  <Trash2 className="h-4 w-4" /> Blk Del
                </button>
              </>
            )}
            <button className="btn-secondary btn-sm" title="Vendor Setup" onClick={() => setVendorSetup({ open: true, id: null })}>
              <Truck className="h-4 w-4" /> Vndrs
            </button>
            <button className="btn-secondary btn-sm" title="Storage locations" onClick={() => setLocsOpen(true)}>
              <MapPin className="h-4 w-4" /> Locs
            </button>
            <button className="btn-secondary btn-sm" title="Document library" onClick={() => setDocsOpen(true)}>
              <FolderOpen className="h-4 w-4" /> Docs
            </button>
            <button className="btn-primary btn-sm" onClick={() => setForm({ open: true, material: null })}>
              <Plus className="h-4 w-4" /> Item
            </button>
          </>
        }
      />

      <div className="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4 no-print">
        <StatCard label="Items tracked" value={materials.isLoading ? '…' : kpi.items.toLocaleString()} icon={<Package className="h-4 w-4" />} />
        <StatCard label="Inventory value" value={materials.isLoading ? '…' : money(kpi.value)} hint="On hand × $/unit" icon={<DollarSign className="h-4 w-4" />} tone="primary" />
        <StatCard
          label="Below minimum"
          value={materials.isLoading ? '…' : kpi.below}
          tone={kpi.below > 0 ? 'danger' : 'success'}
          icon={<AlertTriangle className="h-4 w-4" />}
          hint={
            kpi.below > 0 ? (
              <button className="text-primary hover:underline" onClick={() => setTab('reorder-materials')}>
                Reorder now →
              </button>
            ) : (
              'All stock above minimum'
            )
          }
        />
        <StatCard label="Vendors" value={vendors.isLoading ? '…' : (vendors.data?.length ?? 0)} icon={<Boxes className="h-4 w-4" />} />
      </div>

      <Tabs
        className="mb-4"
        value={tab}
        onChange={setTab}
        tabs={tabLabels.map(([key, label]) => ({ key, label, count: counts[key], hidden: !tabAllowed(key) }))}
      />

      {materials.isError && <ErrorBanner message={errorMessage(materials.error)} />}

      {mt && (
        <DataTable
          rows={rows}
          columns={columns}
          rowKey={(m) => m.id}
          loading={materials.isLoading}
          searchPlaceholder={`Search ${mt.label}…`}
          initialSort={{ key: 'id', dir: 'desc' }}
          selectable={admin}
          selected={selMaterials}
          onSelectedChange={(k) => setSelMaterials(k.map(Number))}
          rowClassName={(m) => (isBelowMin(m) && !selMaterials.includes(m.id) ? 'bg-amber-500/10' : undefined)}
          emptyTitle={`No ${mt.label} yet`}
          emptyDescription={admin ? 'Add items one at a time with “+ Item” or import many at once with “Blk Upld”.' : 'Add items with “+ Item”.'}
          emptyAction={
            <button className="btn-primary" onClick={() => setForm({ open: true, material: null })}>
              <Plus className="h-4 w-4" /> Item
            </button>
          }
        />
      )}
      {tab === 'reorder-materials' && <ReorderTab groupId={groupId} materials={all} loading={materials.isLoading} onPlaced={() => setTab('order-history')} />}
      {tab === 'order-history' && <OrderHistoryTab groupId={groupId} onReorder={() => setTab('reorder-materials')} />}
      {tab === 'vendors' && (
        <VendorsTab
          groupId={groupId}
          selected={selVendors}
          onSelectedChange={setSelVendors}
          onEdit={(id) => setVendorSetup({ open: true, id })}
          onNew={() => setVendorSetup({ open: true, id: null })}
        />
      )}
      {tab === 'reports' && <EnvironmentalReport groupId={groupId} groupName={group?.name} />}

      {/* Modals */}
      <MaterialFormModal open={form.open} onClose={() => setForm({ open: false, material: null })} material={form.material} defaultType={mt?.defaultType} />
      <InventoryModal open={!!inventoryFor} onClose={() => setInventoryFor(null)} material={inventoryFor ? (all.find((m) => m.id === inventoryFor.id) ?? inventoryFor) : null} />
      {docsFor && <EntityDocuments open onClose={() => setDocsFor(null)} entityType="Material" entityId={docsFor.id} groupId={docsFor.groupId} title={`Documents — ${docsFor.productName}`} />}
      {historyFor && <HistoryModal open onClose={() => setHistoryFor(null)} entityType="Material" entityId={historyFor.id} title={`History — ${historyFor.productName}`} />}
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        message={`Are you sure you want to delete the "${deleting?.productName}" material?`}
        busy={removeOne.isPending}
        onConfirm={() => deleting && removeOne.mutate(deleting.id)}
      />
      <ConfirmDialog
        open={bulkDeleting}
        onClose={() => setBulkDeleting(false)}
        message={`Are you sure you want to delete the ${selMaterials.length} selected item${selMaterials.length === 1 ? '' : 's'}?`}
        busy={removeMany.isPending}
        onConfirm={() => removeMany.mutate(selMaterials)}
      />
      <BulkCopyModal
        open={!!bulkCopy}
        onClose={() => setBulkCopy(null)}
        kind={bulkCopy?.kind ?? 'materials'}
        ids={bulkCopy?.ids ?? []}
        onDone={() => (bulkCopy?.kind === 'vendors' ? setSelVendors([]) : setSelMaterials([]))}
      />
      <BulkUploadModal open={bulkUpload} onClose={() => setBulkUpload(false)} groupId={groupId} groupName={group?.name} />
      <VendorSetupModal open={vendorSetup.open} vendorId={vendorSetup.id} onClose={() => setVendorSetup({ open: false, id: null })} />
      <LocationsModal open={locsOpen} onClose={() => setLocsOpen(false)} />
      {docsOpen && <DocumentLibraryModal open onClose={() => setDocsOpen(false)} groupId={groupId} />}
    </>
  )
}
