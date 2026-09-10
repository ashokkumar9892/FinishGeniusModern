import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Package, Pencil, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, inputDate } from '@/lib/format'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { MaterialType } from '@/lib/types'
import type { WiItem, WiItemKind, WiRelatedDoc, WiSignature } from './shared'

// ---------------------------------------------------------------------------------------------
// Building blocks shared by the document page
// ---------------------------------------------------------------------------------------------

export function Block({ title, action, children }: { title: ReactNode; action?: ReactNode; children: ReactNode }) {
  return (
    <div>
      <div className="mb-2 flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-foreground/80">{title}</h3>
        {action}
      </div>
      {children}
    </div>
  )
}

export function NotApplicable() {
  return <div className="rounded-md border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">N/A</div>
}

export function AddButton({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button type="button" className="btn-secondary btn-sm" onClick={onClick} title={label}>
      <Plus className="h-3.5 w-3.5" /> Add
    </button>
  )
}

function RowActions({ onEdit, onDelete }: { onEdit: () => void; onDelete: () => void }) {
  return (
    <div className="flex justify-end gap-0.5">
      <button type="button" className="btn-icon" title="Edit" onClick={onEdit}>
        <Pencil className="h-4 w-4" />
      </button>
      <button type="button" className="btn-icon hover:text-destructive" title="Delete" onClick={onDelete}>
        <Trash2 className="h-4 w-4" />
      </button>
    </div>
  )
}

function SimpleTable({ headers, children }: { headers: ReactNode[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="border-b bg-muted/60">
          <tr>
            {headers.map((h, i) => (
              <th key={i} className="th">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  )
}

/** Save/cancel modal that surfaces server validation in an ErrorBanner. */
function FormModal({ title, formId, busy, error, onClose, onSubmit, children }: {
  title: string
  formId: string
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: () => void
  children: ReactNode
}) {
  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      title={title}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form={formId} disabled={busy}>
            {busy && <Spinner />} Save
          </button>
        </>
      }
    >
      <form
        id={formId}
        className="space-y-4"
        onSubmit={(e: FormEvent) => {
          e.preventDefault()
          onSubmit()
        }}
      >
        <ErrorBanner message={error} />
        {children}
      </form>
    </Modal>
  )
}

/** Runs a save request and keeps the modal open with the error message on failure. */
function useSubmit(save: () => Promise<unknown>, onDone: () => void) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const submit = async (validate?: () => string | null) => {
    const invalid = validate?.() ?? null
    setError(invalid)
    if (invalid) return
    setBusy(true)
    try {
      await save()
      onDone()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }
  return { busy, error, submit }
}

export type SaveFn = (method: 'post' | 'put', url: string, body: unknown) => Promise<unknown>

// ---------------------------------------------------------------------------------------------
// Related documents
// ---------------------------------------------------------------------------------------------

export function RelatedDocsTable({ rows, edit, onEdit, onDelete }: {
  rows: WiRelatedDoc[]
  edit: boolean
  onEdit: (r: WiRelatedDoc) => void
  onDelete: (r: WiRelatedDoc) => void
}) {
  if (!rows.length) return <NotApplicable />
  return (
    <SimpleTable headers={['Document Number', 'Document Name', 'Author', ...(edit ? [''] : [])]}>
      {rows.map((r) => (
        <tr key={r.id} className="border-b last:border-0">
          <td className="td font-medium">{r.documentNumber}</td>
          <td className="td">{r.documentName}</td>
          <td className="td text-muted-foreground">{r.author}</td>
          {edit && (
            <td className="td w-24">
              <RowActions onEdit={() => onEdit(r)} onDelete={() => onDelete(r)} />
            </td>
          )}
        </tr>
      ))}
    </SimpleTable>
  )
}

export function RelatedDocModal({ docId, row, onClose, save }: { docId: number; row: WiRelatedDoc | null; onClose: () => void; save: SaveFn }) {
  const [documentNumber, setDocumentNumber] = useState(row?.documentNumber ?? '')
  const [documentName, setDocumentName] = useState(row?.documentName ?? '')
  const [author, setAuthor] = useState(row?.author ?? '')
  const base = `/work-instructions/${docId}/related-documents`
  const { busy, error, submit } = useSubmit(
    () => save(row ? 'put' : 'post', row ? `${base}/${row.id}` : base, { documentNumber, documentName, author }),
    onClose,
  )
  return (
    <FormModal
      title={row ? 'Edit Related Document' : 'Add Related Document'}
      formId="wi-related"
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={() => submit(() => (documentName.trim() ? null : 'Document Name is required.'))}
    >
      <Field label="Document Number">
        <input className="input" value={documentNumber} maxLength={400} onChange={(e) => setDocumentNumber(e.target.value)} autoFocus />
      </Field>
      <Field label="Document Name" required>
        <input className="input" value={documentName} maxLength={400} onChange={(e) => setDocumentName(e.target.value)} />
      </Field>
      <Field label="Author">
        <input className="input" value={author} maxLength={400} onChange={(e) => setAuthor(e.target.value)} />
      </Field>
    </FormModal>
  )
}

// ---------------------------------------------------------------------------------------------
// Department approval signatures
// ---------------------------------------------------------------------------------------------

export function SignaturesTable({ rows, edit, onEdit, onDelete }: {
  rows: WiSignature[]
  edit: boolean
  onEdit: (r: WiSignature) => void
  onDelete: (r: WiSignature) => void
}) {
  if (!rows.length) return <NotApplicable />
  return (
    <SimpleTable headers={['Name', 'Position', 'Date', ...(edit ? [''] : [])]}>
      {rows.map((r) => (
        <tr key={r.id} className="border-b last:border-0">
          <td className="td font-medium">{r.name}</td>
          <td className="td">{r.position}</td>
          <td className="td whitespace-nowrap text-muted-foreground">{date(r.date)}</td>
          {edit && (
            <td className="td w-24">
              <RowActions onEdit={() => onEdit(r)} onDelete={() => onDelete(r)} />
            </td>
          )}
        </tr>
      ))}
    </SimpleTable>
  )
}

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function SignatureModal({ docId, row, onClose, save }: { docId: number; row: WiSignature | null; onClose: () => void; save: SaveFn }) {
  const [name, setName] = useState(row?.name ?? '')
  const [position, setPosition] = useState(row?.position ?? '')
  const [day, setDay] = useState(row ? inputDate(row.date) : today())
  const base = `/work-instructions/${docId}/signatures`
  const { busy, error, submit } = useSubmit(
    () => save(row ? 'put' : 'post', row ? `${base}/${row.id}` : base, { name, position, date: day || null }),
    onClose,
  )
  return (
    <FormModal
      title={row ? 'Edit Approval Signature' : 'Add Approval Signature'}
      formId="wi-signature"
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={() => submit(() => (name.trim() ? null : 'Name is required.'))}
    >
      <Field label="Name" required>
        <input className="input" value={name} maxLength={400} onChange={(e) => setName(e.target.value)} autoFocus />
      </Field>
      <Field label="Position">
        <input className="input" value={position} maxLength={400} onChange={(e) => setPosition(e.target.value)} />
      </Field>
      <Field label="Date">
        <input className="input" type="date" value={day} onChange={(e) => setDay(e.target.value)} />
      </Field>
    </FormModal>
  )
}

// ---------------------------------------------------------------------------------------------
// Approved tools & equipment / materials
// ---------------------------------------------------------------------------------------------

export function ItemsTable({ rows, edit, onEdit, onDelete }: {
  rows: WiItem[]
  edit: boolean
  onEdit: (r: WiItem) => void
  onDelete: (r: WiItem) => void
}) {
  if (!rows.length) return <NotApplicable />
  return (
    <ul className="divide-y rounded-md border">
      {rows.map((r) => (
        <li key={r.id} className="flex items-center gap-3 px-3 py-2">
          <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-muted text-muted-foreground">
            <Package className="h-3.5 w-3.5" />
          </span>
          <div className="min-w-0 flex-1">
            <div className="text-sm font-medium">{r.description}</div>
            {r.materialName && r.materialName !== r.description && (
              <div className="truncate text-xs text-muted-foreground">Equipment &amp; Materials: {r.materialName}</div>
            )}
          </div>
          {r.materialId && (
            <span className="badge hidden bg-sky-100 text-sky-800 dark:bg-sky-500/15 dark:text-sky-300 sm:inline-flex" title="Linked to the Equipment & Materials list">
              Linked
            </span>
          )}
          {edit && <RowActions onEdit={() => onEdit(r)} onDelete={() => onDelete(r)} />}
        </li>
      ))}
    </ul>
  )
}

interface MaterialOption {
  id: number
  productName: string
  productCode?: string | null
  materialType: number
  materialTypeLabel: string
  categoryName?: string | null
}

export function ItemModal({ docId, groupId, kind, row, onClose, save }: {
  docId: number
  groupId: number
  kind: WiItemKind
  row: WiItem | null
  onClose: () => void
  save: SaveFn
}) {
  const [materialId, setMaterialId] = useState<number | null>(row?.materialId ?? null)
  const [description, setDescription] = useState(row?.description ?? '')
  const equipment = kind === 'Equipment'

  const materials = useQuery({
    queryKey: ['materials', groupId, equipment ? MaterialType.Equipment : 'all'],
    queryFn: () =>
      api
        .get<MaterialOption[]>('/materials', { params: { groupId, type: equipment ? MaterialType.Equipment : undefined } })
        .then((r) => r.data),
  })
  const options = useMemo(
    () =>
      (materials.data ?? [])
        .filter((m) => (equipment ? m.materialType === MaterialType.Equipment : m.materialType !== MaterialType.Equipment))
        .sort((a, b) => a.productName.localeCompare(b.productName))
        .map((m) => ({
          value: m.id,
          label: m.productName,
          sub: [m.materialTypeLabel, m.productCode, m.categoryName].filter(Boolean).join(' · '),
        })),
    [materials.data, equipment],
  )

  const base = `/work-instructions/${docId}/items`
  const { busy, error, submit } = useSubmit(
    () => save(row ? 'put' : 'post', row ? `${base}/${row.id}` : base, { kind, description, materialId }),
    onClose,
  )
  const label = equipment ? 'Tool / Equipment' : 'Material'

  return (
    <FormModal
      title={`${row ? 'Edit' : 'Add'} ${label}`}
      formId="wi-item"
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={() => submit(() => (description.trim() ? null : 'Description is required.'))}
    >
      <Field
        label={`Pick from Equipment & Materials`}
        hint={materials.isError ? errorMessage(materials.error) : `Optional — or just type a description below.`}
      >
        <SearchSelect
          options={options}
          value={materialId}
          placeholder={materials.isLoading ? 'Loading…' : equipment ? 'Select equipment…' : 'Select a material…'}
          emptyText={equipment ? 'No equipment in this group' : 'No materials in this group'}
          onChange={(v) => {
            setMaterialId(v)
            const picked = options.find((o) => o.value === v)
            if (picked) setDescription(picked.label)
          }}
        />
      </Field>
      <Field label="Description" required>
        <input className="input" value={description} maxLength={400} onChange={(e) => setDescription(e.target.value)} placeholder={`${label} description`} />
      </Field>
    </FormModal>
  )
}
