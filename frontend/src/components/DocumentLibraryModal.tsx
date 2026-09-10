import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Download, Eye, FolderOpen, Link2, Pencil, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { ConfirmDialog, ErrorBanner, Modal } from './ui'
import { DataTable, type Column } from './DataTable'
import { SearchSelect } from './SearchSelect'
import { useToast } from './toast'
import { DocumentFormModal } from './documents/DocumentFormModal'
import { LinkDocumentModal } from './documents/LinkDocumentModal'
import { FilePreviewModal } from './documents/FilePreviewModal'
import { DocThumb } from './documents/FileVisuals'
import { docKind, docPreview, formatBytes, type DocumentRow, type PreviewSource } from './documents/shared'

/**
 * Central document library ("Docs" on Equipment & Materials): list / create / edit / copy / link / delete
 * documents of a group.
 */
export function DocumentLibraryModal({ open, onClose, groupId }: { open: boolean; onClose: () => void; groupId: number }) {
  const { groups } = useGroup()
  const toast = useToast()
  const qc = useQueryClient()
  const [gid, setGid] = useState(groupId)
  const [form, setForm] = useState<{ doc: DocumentRow | null } | null>(null)
  const [linkDoc, setLinkDoc] = useState<DocumentRow | null>(null)
  const [deleteDoc, setDeleteDoc] = useState<DocumentRow | null>(null)
  const [preview, setPreview] = useState<PreviewSource | null>(null)

  useEffect(() => {
    if (open) setGid(groupId)
  }, [open, groupId])

  const q = useQuery({
    queryKey: ['documents', gid, 'library'],
    queryFn: () => api.get<DocumentRow[]>('/documents', { params: { groupId: gid } }).then((r) => r.data),
    enabled: open && gid > 0,
  })

  const copy = useMutation({
    mutationFn: (id: number) => api.post<{ message: string }>(`/documents/${id}/copy`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['documents'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (id: number) => api.delete<{ message: string }>(`/documents/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleteDoc(null)
      qc.invalidateQueries({ queryKey: ['documents'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  // Keep the Link modal pointed at fresh data after a save (links count / labels).
  const liveLinkDoc = linkDoc ? (q.data?.find((d) => d.id === linkDoc.id) ?? linkDoc) : null

  const columns = useMemo<Column<DocumentRow>[]>(
    () => [
      { key: 'id', header: '#', className: 'w-14 tabular-nums' },
      { key: 'groupName', header: 'Group', hideBelow: 'lg' },
      {
        key: 'name',
        header: 'Name',
        cell: (r) => (
          <div className="min-w-[10rem] max-w-xs">
            <div className="truncate font-medium" title={r.name}>
              {r.name}
            </div>
            <div className="truncate text-xs text-muted-foreground" title={`${r.fileName ?? ''} · ${dateTime(r.createdAt)}`}>
              {r.fileName} · {formatBytes(r.fileSize)}
            </div>
          </div>
        ),
        sortValue: (r) => r.name,
      },
      {
        key: 'thumbnail',
        header: 'Thumbnail',
        sortable: false,
        cell: (r) =>
          docKind(r) === 'none' ? (
            <span className="block text-xs leading-tight text-muted-foreground">
              No preview available
              <br />
              Download Only
            </span>
          ) : (
            <DocThumb doc={r} onClick={() => setPreview(docPreview(r))} />
          ),
      },
      {
        key: 'links',
        header: 'Links',
        align: 'center',
        hideBelow: 'sm',
        sortValue: (r) => r.links.length,
        cell: (r) => (
          <button
            type="button"
            className={r.links.length ? 'badge bg-accent text-accent-foreground hover:ring-1 hover:ring-primary/40' : 'badge bg-muted text-muted-foreground hover:ring-1 hover:ring-primary/40'}
            title={r.links.length ? r.links.map((l) => l.label).join('\n') : 'Not linked yet — click to link'}
            onClick={() => setLinkDoc(r)}
          >
            {r.links.length} linked
          </button>
        ),
      },
      {
        key: 'actions',
        header: 'Actions',
        sortable: false,
        align: 'right',
        cell: (r) => (
          <div className="flex items-center justify-end gap-0.5">
            {docKind(r) !== 'none' && (
              <button className="btn-icon" title="Preview" onClick={() => setPreview(docPreview(r))}>
                <Eye className="h-4 w-4" />
              </button>
            )}
            {r.storedFile && (
              <a className="btn-icon" title="Download" href={fileUrl(r.storedFile)} download={r.fileName || r.name}>
                <Download className="h-4 w-4" />
              </a>
            )}
            <button className="btn-icon" title="Edit" onClick={() => setForm({ doc: r })}>
              <Pencil className="h-4 w-4" />
            </button>
            <button className="btn-icon" title="Copy" disabled={copy.isPending} onClick={() => copy.mutate(r.id)}>
              <Copy className="h-4 w-4" />
            </button>
            <button className="btn-icon" title="Link" onClick={() => setLinkDoc(r)}>
              <Link2 className="h-4 w-4" />
            </button>
            <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleteDoc(r)}>
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ),
      },
    ],
    [copy],
  )

  const newButton = (
    <button className="btn-primary" onClick={() => setForm({ doc: null })} disabled={!gid}>
      <Plus className="h-4 w-4" /> New Document
    </button>
  )

  return (
    <>
      <Modal open={open} onClose={onClose} title="Documents" size="xl" footer={<button className="btn-secondary" onClick={onClose}>Close</button>}>
        {q.isError && <ErrorBanner message={errorMessage(q.error)} />}
        <DataTable
          bare
          dense
          rows={q.data ?? []}
          loading={q.isLoading}
          columns={columns}
          rowKey={(r) => r.id}
          initialSort={{ key: 'id', dir: 'desc' }}
          searchPlaceholder="Search documents…"
          searchText={(r) => `${r.id} ${r.name} ${r.fileName ?? ''} ${r.groupName} ${r.links.map((l) => l.label).join(' ')}`}
          toolbar={
            <>
              <SearchSelect
                className="w-full sm:w-64"
                clearable={false}
                options={groups.map((g) => ({ value: g.id, label: g.name }))}
                value={gid}
                onChange={(v) => v && setGid(v)}
                placeholder="Select group"
              />
              {newButton}
            </>
          }
          emptyTitle="No documents in this group yet"
          emptyDescription="Upload safety data sheets, tech data, photos or videos once and link them to materials, formulations and processes."
          emptyAction={newButton}
        />
        <p className="mt-2 flex items-center gap-1.5 text-xs text-muted-foreground">
          <FolderOpen className="h-3.5 w-3.5" /> Linked documents appear in the “Documents” action of each material, formulation, process step and schedule.
        </p>
      </Modal>

      <DocumentFormModal open={!!form} onClose={() => setForm(null)} groupId={gid} doc={form?.doc} />
      <LinkDocumentModal open={!!linkDoc} onClose={() => setLinkDoc(null)} doc={liveLinkDoc} />
      <ConfirmDialog
        open={!!deleteDoc}
        onClose={() => setDeleteDoc(null)}
        busy={remove.isPending}
        onConfirm={() => deleteDoc && remove.mutate(deleteDoc.id)}
        message={
          <>
            Are you sure you want to delete the "{deleteDoc?.name}" document?
            {!!deleteDoc?.links.length && (
              <span className="mt-2 block text-muted-foreground">
                It is linked to {deleteDoc.links.length} object{deleteDoc.links.length === 1 ? '' : 's'}; those links will be removed too.
              </span>
            )}
          </>
        }
      />
      <FilePreviewModal source={preview} onClose={() => setPreview(null)} />
    </>
  )
}
