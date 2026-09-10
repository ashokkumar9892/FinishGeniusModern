import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { AxiosProgressEvent } from 'axios'
import { Download, Eye, FileText, Link2, Unlink, Upload } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { dateTime } from '@/lib/format'
import type { LinkEntityType } from '@/lib/types'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note, Spinner, Tabs } from './ui'
import { FileDrop } from './FileDrop'
import { SearchSelect } from './SearchSelect'
import { useToast } from './toast'
import { FilePreviewModal } from './documents/FilePreviewModal'
import { DocThumb, ProgressBar } from './documents/FileVisuals'
import {
  MAX_DOCUMENT_BYTES, docKind, docPreview, entityTypeLabel, formatBytes, tooLargeMessage, uploadPercent,
  type DocumentRow, type PreviewSource,
} from './documents/shared'

/**
 * "Documents" row action shared by Materials, Formulas, Process Steps, Schedules, Pricing and
 * Material Quantities: lists documents linked to one object and lets the user upload/link/unlink.
 */
export function EntityDocuments({
  open,
  onClose,
  entityType,
  entityId,
  groupId,
  title,
}: {
  open: boolean
  onClose: () => void
  entityType: LinkEntityType
  /** 0 for page-level documents (e.g. Pricing / Material Quantities of a group). */
  entityId: number
  groupId: number
  title?: string
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [tab, setTab] = useState<'upload' | 'link'>('upload')
  const [name, setName] = useState('')
  const [files, setFiles] = useState<File[]>([])
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pick, setPick] = useState<number | null>(null)
  const [preview, setPreview] = useState<PreviewSource | null>(null)

  useEffect(() => {
    if (!open) return
    setTab('upload')
    setName('')
    setFiles([])
    setProgress(null)
    setError(null)
    setPick(null)
  }, [open, entityType, entityId])

  const params = { groupId, entityType, entityId }
  const linked = useQuery({
    queryKey: ['documents', groupId, entityType, entityId],
    queryFn: () => api.get<DocumentRow[]>('/documents', { params }).then((r) => r.data),
    enabled: open && groupId > 0,
  })
  const library = useQuery({
    queryKey: ['documents', groupId, 'library'],
    queryFn: () => api.get<DocumentRow[]>('/documents', { params: { groupId } }).then((r) => r.data),
    enabled: open && groupId > 0,
  })

  const docs = useMemo(() => linked.data ?? [], [linked.data])
  const linkedIds = useMemo(() => new Set(docs.map((d) => d.id)), [docs])
  const available = useMemo(() => (library.data ?? []).filter((d) => !linkedIds.has(d.id)), [library.data, linkedIds])

  const refresh = () => qc.invalidateQueries({ queryKey: ['documents'] })

  const upload = useMutation({
    mutationFn: async () => {
      const file = files[0]
      const fd = new FormData()
      fd.append('groupId', String(groupId))
      fd.append('name', name.trim() || file.name)
      fd.append('file', file)
      fd.append('entityType', entityType)
      fd.append('entityId', String(entityId))
      const res = await api.post<{ message: string }>('/documents', fd, {
        onUploadProgress: (e: AxiosProgressEvent) => setProgress(uploadPercent(e.loaded, e.total, file.size)),
      })
      return res.data
    },
    onMutate: () => {
      setError(null)
      setProgress(0)
    },
    onSuccess: (res) => {
      toast.success(res.message)
      setFiles([])
      setName('')
      setProgress(null)
      refresh()
    },
    onError: (e) => {
      setError(errorMessage(e))
      setProgress(null)
    },
  })

  const link = useMutation({
    mutationFn: (id: number) => api.post<{ message: string }>(`/documents/${id}/link`, { entityType, entityId }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setPick(null)
      refresh()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const unlink = useMutation({
    mutationFn: (id: number) => api.delete<{ message: string }>(`/documents/${id}/link`, { params: { entityType, entityId } }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const onFiles = (list: File[]) => {
    const f = list[0]
    if (f && f.size > MAX_DOCUMENT_BYTES) {
      setError(tooLargeMessage(f))
      return
    }
    setError(null)
    setFiles(list)
    if (f && !name.trim()) setName(f.name)
  }

  const objectLabel = entityTypeLabel[entityType] ?? entityType
  const heading = title ? (/^documents\b/i.test(title) ? title : `Documents — ${title}`) : entityId === 0 ? `Documents — ${objectLabel}` : 'Documents'
  const busy = upload.isPending

  return (
    <>
      <Modal open={open} onClose={() => !busy && onClose()} title={heading} size="lg" footer={<button className="btn-secondary" onClick={onClose} disabled={busy}>Close</button>}>
        {entityId === 0 && (
          <div className="mb-3">
            <Note tone="info">These documents belong to the group’s {objectLabel} page and are shared by everyone working on it.</Note>
          </div>
        )}

        <div className="mb-2 flex items-center justify-between">
          <h4 className="text-sm font-semibold">
            Linked documents <span className="badge ml-1 bg-muted text-muted-foreground">{docs.length}</span>
          </h4>
          {library.data && <span className="text-xs text-muted-foreground">{library.data.length} in the group library</span>}
        </div>

        {linked.isLoading ? (
          <LoadingBlock />
        ) : linked.isError ? (
          <ErrorBanner message={errorMessage(linked.error)} />
        ) : docs.length === 0 ? (
          <div className="rounded-md border border-dashed">
            <EmptyState title="No documents linked yet" description="Upload a new file or link one from the group library below." icon={<FileText className="h-5 w-5" />} />
          </div>
        ) : (
          <ul className="divide-y rounded-md border">
            {docs.map((d) => {
              const canPreview = docKind(d) !== 'none'
              const others = d.links.length - 1
              return (
                <li key={d.id} className="flex items-center gap-3 p-2.5">
                  <DocThumb doc={d} onClick={canPreview ? () => setPreview(docPreview(d)) : undefined} />
                  <div className="min-w-0 flex-1">
                    <div className="truncate font-medium" title={d.name}>
                      {d.name}
                    </div>
                    <div className="truncate text-xs text-muted-foreground">
                      {d.fileName} · {formatBytes(d.fileSize)} · {dateTime(d.createdAt)}
                      {d.createdByName ? ` · ${d.createdByName}` : ''}
                    </div>
                    {others > 0 && (
                      <div className="truncate text-xs text-muted-foreground" title={d.links.map((l) => l.label).join('\n')}>
                        Also linked to {others} other object{others === 1 ? '' : 's'}
                      </div>
                    )}
                  </div>
                  <div className="flex shrink-0 items-center gap-0.5">
                    {canPreview && (
                      <button className="btn-icon" title="Preview" onClick={() => setPreview(docPreview(d))}>
                        <Eye className="h-4 w-4" />
                      </button>
                    )}
                    {d.storedFile && (
                      <a className="btn-icon" title="Download" href={fileUrl(d.storedFile)} download={d.fileName || d.name}>
                        <Download className="h-4 w-4" />
                      </a>
                    )}
                    <button
                      className="btn-icon hover:text-destructive"
                      title="Unlink (the document stays in the library)"
                      disabled={unlink.isPending && unlink.variables === d.id}
                      onClick={() => unlink.mutate(d.id)}
                    >
                      {unlink.isPending && unlink.variables === d.id ? <Spinner /> : <Unlink className="h-4 w-4" />}
                    </button>
                  </div>
                </li>
              )
            })}
          </ul>
        )}

        <div className="mt-5 rounded-md border">
          <Tabs
            className="px-2"
            value={tab}
            onChange={(t) => {
              setTab(t)
              setError(null)
            }}
            tabs={[
              { key: 'upload', label: 'Upload new' },
              { key: 'link', label: 'Link existing', count: library.data ? available.length : undefined },
            ]}
          />
          <div className="p-3">
            <ErrorBanner message={error} />
            {tab === 'upload' ? (
              <div className="space-y-3">
                <Field label="Name" hint="Defaults to the file name.">
                  <input className="input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} placeholder="Document name" disabled={busy} />
                </Field>
                <FileDrop files={files} onChange={onFiles} label="Drag & drop a file here … (any type, max 100 MB)" />
                {progress !== null && <ProgressBar value={progress} />}
                <div className="flex justify-end">
                  <button className="btn-primary" disabled={!files.length || busy} onClick={() => upload.mutate()}>
                    {busy ? <Spinner /> : <Upload className="h-4 w-4" />} Upload & link
                  </button>
                </div>
              </div>
            ) : (
              <div className="flex flex-col gap-2 sm:flex-row">
                <SearchSelect
                  className="flex-1"
                  options={available.map((d) => ({ value: d.id, label: d.name, sub: `${d.fileName ?? ''} · ${formatBytes(d.fileSize)}` }))}
                  value={pick}
                  onChange={setPick}
                  placeholder={library.isLoading ? 'Loading library…' : available.length ? 'Choose a document from the library…' : 'All group documents are already linked'}
                  disabled={library.isLoading || available.length === 0}
                  emptyText="No documents match"
                />
                <button className="btn-primary" disabled={!pick || link.isPending} onClick={() => pick && link.mutate(pick)}>
                  {link.isPending ? <Spinner /> : <Link2 className="h-4 w-4" />} Link
                </button>
              </div>
            )}
          </div>
        </div>
      </Modal>
      <FilePreviewModal source={preview} onClose={() => setPreview(null)} />
    </>
  )
}
