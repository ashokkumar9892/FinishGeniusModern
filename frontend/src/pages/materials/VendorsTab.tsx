import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, Trash2, Truck } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useMe } from '@/lib/auth'
import { isAdmin } from '@/lib/access'
import { DataTable, type Column } from '@/components/DataTable'
import { ConfirmDialog } from '@/components/ui'
import { useToast } from '@/components/toast'
import { useVendors, type MessageResponse, type Vendor } from './shared'

export function VendorsTab({ groupId, selected, onSelectedChange, onEdit, onNew }: {
  groupId: number
  selected: number[]
  onSelectedChange: (ids: number[]) => void
  onEdit: (id: number) => void
  onNew: () => void
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const me = useMe()
  const admin = isAdmin(me)
  const vendors = useVendors(groupId)
  const [deleting, setDeleting] = useState<Vendor | null>(null)

  const remove = useMutation({
    mutationFn: (id: number) => api.delete<MessageResponse>(`/vendors/${id}`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setDeleting(null)
      onSelectedChange([])
      qc.invalidateQueries({ queryKey: ['vendors'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const columns: Column<Vendor>[] = [
    { key: 'id', header: '#', className: 'w-14 tabular-nums' },
    { key: 'vendorName', header: 'Vendor Name', cell: (v) => <span className="font-medium">{v.vendorName}</span> },
    { key: 'paymentTerms', header: 'Payment Terms', hideBelow: 'md' },
    { key: 'accountNumber', header: 'Account #', hideBelow: 'lg' },
    { key: 'groupName', header: 'Group Name', hideBelow: 'xl' },
    { key: 'contactName', header: 'Contact Name', hideBelow: 'sm' },
    { key: 'officePhone', header: 'Office Phone', hideBelow: 'md', cell: (v) => <span className="whitespace-nowrap">{v.officePhone}</span> },
    { key: 'mobilePhone', header: 'Mobile Phone', hideBelow: 'xl', cell: (v) => <span className="whitespace-nowrap">{v.mobilePhone}</span> },
    { key: 'vendorEmail', header: 'Vendor Email', hideBelow: 'lg', cell: (v) => (v.vendorEmail ? <a className="text-primary hover:underline" href={`mailto:${v.vendorEmail}`}>{v.vendorEmail}</a> : null) },
    { key: 'requestorEmail', header: 'Requestor Email', hideBelow: 'xl' },
    { key: 'address', header: 'Address', hideBelow: 'xl' },
    { key: 'state', header: 'State', hideBelow: 'lg' },
    { key: 'city', header: 'City', hideBelow: 'lg' },
    { key: 'zip', header: 'Zip', hideBelow: 'xl' },
    {
      key: 'actions',
      header: 'Actions',
      sortable: false,
      align: 'right',
      cell: (v) => (
        <div className="flex justify-end gap-0.5">
          <button className="btn-icon" title="Edit" onClick={() => onEdit(v.id)}>
            <Pencil className="h-4 w-4" />
          </button>
          {admin && (
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(v)}>
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      ),
    },
  ]

  return (
    <>
      <DataTable
        rows={vendors.data ?? []}
        columns={columns}
        rowKey={(v) => v.id}
        loading={vendors.isLoading}
        searchPlaceholder="Search vendors…"
        initialSort={{ key: 'vendorName', dir: 'asc' }}
        selectable={admin}
        selected={selected}
        onSelectedChange={(keys) => onSelectedChange(keys.map(Number))}
        emptyTitle="No vendors yet"
        emptyDescription="Vendors are used when placing purchase orders from Reorder Materials."
        emptyAction={
          <button className="btn-primary" onClick={onNew}>
            <Truck className="h-4 w-4" /> Add vendor
          </button>
        }
        toolbar={
          <button className="btn-secondary btn-sm" onClick={onNew}>
            <Plus className="h-4 w-4" /> New Vendor
          </button>
        }
      />
      {vendors.isError && <p className="mt-2 text-sm text-destructive">{errorMessage(vendors.error)}</p>}
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        message={`Are you sure you want to delete the "${deleting?.vendorName}" vendor?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </>
  )
}
