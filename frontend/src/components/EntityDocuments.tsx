import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { AxiosProgressEvent } from 'axios'
import { Download, Eye, FileText, Link2, Maximize2, Unlink, Upload } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { useMe } from '@/lib/auth'
import { isSystemAdmin } from '@/lib/access'
import { dateTime } from '@/lib/format'
import type { LinkEntityType } from '@/lib/types'
import { EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Note, SearchInput, Spinner, Tabs } from './ui'
import { FileDrop } from './FileDrop'
import { useToast } from './toast'
import { FilePreviewModal } from './documents/FilePreviewModal'
import { DocumentSlideshow } from './documents/DocumentSlideshow'
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
  const me = useMe()
  // Legacy: System Admins choose from every group's documents; another group's document is copied into this group when linked.
  const allGroups = isSystemAdmin(me)
  const [tab, setTab] = useState<'upload' | 'link'>('upload')
  const [name, setName] = useState('')
  const [files, setFiles] = useState<File[]>([])
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [picked, setPicked] = useState<Set<number>>(new Set())
  const [dirSearch, setDirSearch] = useState('')
  const [preview, setPreview] = useState<PreviewSource | null>(null)
  const [slideshow, setSlideshow] = useState<number | null>(null)

  useEffect(() => {
    if (!open) return
    setTab('upload')
    setName('')
    setFiles([])
    setProgress(null)
    setError(null)
    setPicked(new Set())
    setDirSearch('')
    setSlideshow(null)
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
  // System Admins search every group's documents on the server (the whole library is far too large to load).
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const t = setTimeout(() => setDebounced(dirSearch.trim()), 300)
    return () => clearTimeout(t)
  }, [dirSearch])
  const searchAll = allGroups && debounced.length >= 2
  const everywhere = useQuery({
    queryKey: ['documents', 'all-groups', debounced],
    queryFn: () => api.get<DocumentRow[]>('/documents', { params: { search: debounced, take: 200 } }).then((r) => r.data),
    enabled: open && searchAll,
  })

  const docs = useMemo(() => linked.data ?? [], [linked.data])
  const linkedIds = useMemo(() => new Set(docs.map((d) => d.id)), [docs])
  const available = useMemo(() => (library.data ?? []).filter((d) => !linkedIds.has(d.id)), [library.data, linkedIds])
  const docLabel = (d: DocumentRow) => (allGroups ? `[${d.groupName}] ${d.name}` : d.name)
  const needle = dirSearch.trim().toLowerCase()
  const directory = useMemo(() => {
    const pool = searchAll ? (everywhere.data ?? []).filter((d) => !linkedIds.has(d.id)) : available
    return pool.filter((d) => !needle || `${docLabel(d)} ${d.fileName ?? ''}`.toLowerCase().includes(needle))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [available, everywhere.data, searchAll, linkedIds, needle, allGroups])
  const directoryLoading = library.isLoading || (searchAll && everywhere.isLoading)
  const groupCount = library.data?.length ?? 0

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
    mutationFn: (ids: number[]) =>
      api.post<{ message: string; copied: number }>('/documents/link-many', { groupId, entityType, entityId, documentIds: ids }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setPicked(new Set())
      refresh()
    },
    onError: (e) => setError(errorMessage(e)),
  })
  const togglePick = (id: number) =>
    setPicked((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
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
          <div className="flex items-center gap-2">
            {library.data && <span className="hidden text-xs text-muted-foreground sm:inline">{groupCount} in the group library</span>}
            {docs.length > 0 && (
              <button className="btn-secondary btn-sm" onClick={() => setSlideshow(0)} title="Show every linked document full screen">
                <Maximize2 className="h-4 w-4" /> Fullscreen View
              </button>
            )}
          </div>
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
              { key: 'link', label: 'Choose from Doc Directory', count: library.data ? available.length : undefined },
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
              <div className="space-y-2">
                <p className="text-sm">Please select the Documents you wish to link to this {objectLabel === 'Formula' ? 'Formulation' : objectLabel}.</p>
                {allGroups && (
                  <Note tone="info">
                    As a System Administrator you can search the documents of every group (type at least 2 characters); a document of another group is copied into this
                    group when you link it.
                  </Note>
                )}
                <SearchInput value={dirSearch} onChange={setDirSearch} placeholder="Search documents…" />
                {directoryLoading ? (
                  <LoadingBlock label="Loading documents…" />
                ) : !searchAll && available.length === 0 ? (
                  <p className="py-3 text-center text-sm text-muted-foreground">All documents of this group are already linked.</p>
                ) : (
                  <ul className="max-h-64 divide-y overflow-y-auto rounded-md border" aria-label="Doc Directory">
                    {directory.length === 0 && <li className="px-3 py-4 text-center text-sm text-muted-foreground">No documents match “{dirSearch}”.</li>}
                    {directory.slice(0, 500).map((d) => (
                      <li key={d.id}>
                        <label className="flex cursor-pointer items-center gap-2 px-2.5 py-1.5 text-sm hover:bg-muted/60">
                          <input type="checkbox" className="h-4 w-4 shrink-0 accent-[hsl(var(--primary))]" checked={picked.has(d.id)} onChange={() => togglePick(d.id)} />
                          <span className="min-w-0 flex-1 truncate" title={docLabel(d)}>
                            {docLabel(d)}
                          </span>
                          <span className="hidden shrink-0 text-xs text-muted-foreground sm:inline">
                            {d.fileName} · {formatBytes(d.fileSize)}
                          </span>
                        </label>
                      </li>
                    ))}
                  </ul>
                )}
                <div className="flex items-center justify-end gap-2">
                  <span className="mr-auto text-xs text-muted-foreground">{picked.size} selected</span>
                  <button className="btn-primary" disabled={picked.size === 0 || link.isPending} onClick={() => link.mutate([...picked])}>
                    {link.isPending ? <Spinner /> : <Link2 className="h-4 w-4" />} Save
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      </Modal>
      <FilePreviewModal source={preview} onClose={() => setPreview(null)} />
      <DocumentSlideshow open={slideshow !== null} docs={docs} start={slideshow ?? 0} title={title} onClose={() => setSlideshow(null)} />
    </>
  )
}
