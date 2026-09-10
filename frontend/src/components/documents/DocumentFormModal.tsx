import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { AxiosProgressEvent } from 'axios'
import { Maximize2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { ErrorBanner, Field, Modal, Spinner } from '../ui'
import { FileDrop } from '../FileDrop'
import { useToast } from '../toast'
import { FilePreviewModal } from './FilePreviewModal'
import { FileTypeTile, ProgressBar } from './FileVisuals'
import {
  MAX_DOCUMENT_BYTES, docKind, formatBytes, previewKindOf, tooLargeMessage, uploadPercent,
  type DocumentRow, type PreviewSource,
} from './shared'

/**
 * "Create & Edit Document": name + drag & drop file, a working Fullscreen View of the selected (or current) file and
 * an upload progress bar. Pass `doc` to edit (the file is optional and replaces the old one); `link` links a new
 * document to an object right away.
 */
export function DocumentFormModal({ open, onClose, groupId, doc, link, onSaved }: {
  open: boolean
  onClose: () => void
  groupId: number
  doc?: DocumentRow | null
  link?: { entityType: string; entityId: number }
  onSaved?: (doc: DocumentRow) => void
}) {
  const toast = useToast()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [files, setFiles] = useState<File[]>([])
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [nameError, setNameError] = useState<string | null>(null)
  const [preview, setPreview] = useState<PreviewSource | null>(null)

  useEffect(() => {
    if (!open) return
    setName(doc?.name ?? '')
    setFiles([])
    setProgress(null)
    setError(null)
    setNameError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, doc?.id])

  const file = files[0]
  const localUrl = useMemo(() => (file ? URL.createObjectURL(file) : null), [file])
  useEffect(() => () => { if (localUrl) URL.revokeObjectURL(localUrl) }, [localUrl])

  const current: (PreviewSource & { size: number; isNew: boolean }) | null =
    file && localUrl
      ? { src: localUrl, name: file.name, kind: previewKindOf(file.name, file.type), size: file.size, isNew: true }
      : doc?.storedFile
        ? {
            src: fileUrl(doc.storedFile), name: doc.fileName || doc.name, kind: docKind(doc), size: doc.fileSize, isNew: false,
            downloadHref: fileUrl(doc.storedFile), downloadName: doc.fileName || doc.name,
          }
        : null

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

  const save = useMutation({
    mutationFn: async () => {
      const fd = new FormData()
      fd.append('name', name.trim())
      if (file) fd.append('file', file)
      const config = {
        onUploadProgress: (e: AxiosProgressEvent) => file && setProgress(uploadPercent(e.loaded, e.total, file.size)),
      }
      if (doc) return (await api.put<{ message: string; document: DocumentRow }>(`/documents/${doc.id}`, fd, config)).data
      fd.append('groupId', String(groupId))
      if (link) {
        fd.append('entityType', link.entityType)
        fd.append('entityId', String(link.entityId))
      }
      return (await api.post<{ message: string; document: DocumentRow }>('/documents', fd, config)).data
    },
    onMutate: () => {
      setError(null)
      setProgress(file ? 0 : null)
    },
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['documents'] })
      onSaved?.(res.document)
      onClose()
    },
    onError: (e) => {
      setError(errorMessage(e))
      setProgress(null)
    },
  })

  const submit = () => {
    const n = name.trim()
    setNameError(!n ? 'Name is required.' : n.length > 200 ? 'Name must be 200 characters or fewer.' : null)
    if (!doc && !file) setError('Please choose a file to upload.')
    if (!n || n.length > 200 || (!doc && !file)) return
    save.mutate()
  }

  const busy = save.isPending
  return (
    <>
      <Modal
        open={open}
        onClose={() => !busy && onClose()}
        title="Create & Edit Document"
        size="md"
        footer={
          <>
            <button className="btn-secondary" onClick={onClose} disabled={busy}>
              Cancel
            </button>
            <button className="btn-primary" onClick={submit} disabled={busy}>
              {busy && <Spinner />} {doc ? 'Save' : 'Upload'}
            </button>
          </>
        }
      >
        <ErrorBanner message={error} />
        <Field label="Name" required error={nameError}>
          <input
            className={nameError ? 'input input-invalid' : 'input'}
            value={name}
            maxLength={200}
            placeholder="e.g. Kemvar 9320S Safety Data Sheet"
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
            autoFocus
          />
        </Field>
        <Field
          label={doc ? 'Replace file (optional)' : 'File'}
          required={!doc}
          className="mt-4"
          hint="Any file type up to 100 MB. Images, PDFs and MP4 videos can be previewed."
        >
          <FileDrop files={files} onChange={onFiles} label="Drag & drop a file here …" />
        </Field>

        {current && (
          <div className="mt-4 flex items-center gap-3 rounded-md border bg-muted/30 p-3">
            {current.kind === 'image' ? (
              <img src={current.src} alt="" className="h-14 w-20 shrink-0 rounded-md border object-cover bg-muted" />
            ) : (
              <FileTypeTile name={current.name} className="h-14 w-20" />
            )}
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-medium" title={current.name}>
                {current.name}
              </div>
              <div className="text-xs text-muted-foreground">
                {current.isNew ? (doc ? 'New file — replaces the current one when saved' : 'Selected file — not uploaded yet') : 'Current file'} ·{' '}
                {formatBytes(current.size)}
              </div>
            </div>
            <button type="button" className="btn-secondary btn-sm" onClick={() => setPreview(current)}>
              <Maximize2 className="h-3.5 w-3.5" /> Fullscreen View
            </button>
          </div>
        )}

        {progress !== null && (
          <div className="mt-4">
            <ProgressBar value={progress} />
          </div>
        )}
      </Modal>
      <FilePreviewModal source={preview} onClose={() => setPreview(null)} />
    </>
  )
}
