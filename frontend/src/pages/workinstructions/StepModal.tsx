import { useState, type FormEvent } from 'react'
import { Film, ImageIcon, Trash2 } from 'lucide-react'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { FileDrop } from '@/components/FileDrop'
import { useToast } from '@/components/toast'
import { MEDIA_EXT, type ApiMessage, type WiStep } from './shared'

/**
 * "Edit Work Instruction" — adds or edits one step, uploads its media and lists existing media.
 * If the step was created but the upload failed, a retry uploads to the same step (no duplicate step).
 */
export function StepModal({ docId, step, nextLevel, onClose, onChanged, onDeleteStep }: {
  docId: number
  /** null = new step */
  step: WiStep | null
  nextLevel: number
  onClose: () => void
  onChanged: () => void
  onDeleteStep: (step: WiStep) => void
}) {
  const toast = useToast()
  const [level, setLevel] = useState(String(step?.level ?? nextLevel))
  const [title, setTitle] = useState(step?.title ?? '')
  const [body, setBody] = useState(step?.body ?? '')
  const [files, setFiles] = useState<File[]>([])
  const [createdId, setCreatedId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmMedia, setConfirmMedia] = useState<number | null>(null)
  const [deletingMedia, setDeletingMedia] = useState<number | null>(null)

  const stepId = step?.id ?? createdId

  const save = async (e?: FormEvent) => {
    e?.preventDefault()
    setError(null)
    if (!title.trim()) return setError('Description / Title is required.')
    const lvl = parseInt(level, 10)
    if (level.trim() && (!Number.isFinite(lvl) || lvl < 1)) return setError('Level must be a whole number of 1 or more.')

    setBusy(true)
    try {
      const payload = { level: lvl > 0 ? lvl : null, title, body }
      let id = stepId
      if (id) await api.put<ApiMessage>(`/work-instructions/steps/${id}`, payload)
      else {
        const res = await api.post<ApiMessage>(`/work-instructions/${docId}/steps`, payload)
        id = res.data.id!
        setCreatedId(id)
      }
      if (files.length) {
        const fd = new FormData()
        files.forEach((f) => fd.append('files', f))
        await api.post<ApiMessage>(`/work-instructions/steps/${id}/media`, fd, {
          onUploadProgress: (ev) => setProgress(ev.total ? Math.round((ev.loaded * 100) / ev.total) : null),
        })
        setFiles([])
      }
      toast.success(step || createdId ? 'Step saved.' : 'Step added.')
      onChanged()
      onClose()
    } catch (err) {
      setError(errorMessage(err))
      onChanged()
    } finally {
      setBusy(false)
      setProgress(null)
    }
  }

  const deleteMedia = async (mediaId: number) => {
    setDeletingMedia(mediaId)
    try {
      const res = await api.delete<ApiMessage>(`/work-instructions/media/${mediaId}`)
      toast.success(res.data.message)
      onChanged()
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setDeletingMedia(null)
      setConfirmMedia(null)
    }
  }

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      title="Edit Work Instruction"
      size="lg"
      footer={
        <>
          {step && (
            <button type="button" className="btn-ghost mr-auto text-destructive hover:bg-destructive/10" onClick={() => onDeleteStep(step)} disabled={busy} title="Delete step">
              <Trash2 className="h-4 w-4" /> Delete Step
            </button>
          )}
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form="wi-step" disabled={busy}>
            {busy && <Spinner />} {files.length ? 'Save & Upload' : 'Save'}
          </button>
        </>
      }
    >
      <form id="wi-step" onSubmit={save} className="space-y-4">
        <ErrorBanner message={error} />
        <div className="grid gap-4 sm:grid-cols-[7rem_minmax(0,1fr)]">
          <Field label="Level">
            <input className="input" type="number" min={1} step={1} value={level} onChange={(e) => setLevel(e.target.value)} />
          </Field>
          <Field label="Description / Title" required>
            <textarea className="input min-h-[72px]" rows={3} maxLength={4000} value={title} onChange={(e) => setTitle(e.target.value)} autoFocus={!step} />
          </Field>
        </div>
        <Field label="Details" hint="Optional instructions shown under the step's photos and videos.">
          <textarea className="input" rows={4} value={body} onChange={(e) => setBody(e.target.value)} />
        </Field>

        {step && step.media.length > 0 && (
          <div>
            <div className="label">Current photos &amp; videos</div>
            <ul className="grid gap-2 sm:grid-cols-2">
              {step.media.map((m) => (
                <li key={m.id} className="flex items-center gap-2 rounded-md border p-1.5 pr-2">
                  {m.isVideo ? (
                    <span className="grid h-10 w-14 shrink-0 place-items-center rounded bg-slate-900 text-white">
                      <Film className="h-4 w-4" />
                    </span>
                  ) : (
                    <img src={fileUrl(m.storedFile)} alt="" className="h-10 w-14 shrink-0 rounded object-cover" />
                  )}
                  <span className="min-w-0 flex-1 truncate text-xs" title={m.fileName}>
                    {m.isVideo ? <Film className="mr-1 inline h-3 w-3" /> : <ImageIcon className="mr-1 inline h-3 w-3" />}
                    {m.fileName}
                  </span>
                  {confirmMedia === m.id ? (
                    <span className="flex items-center gap-1">
                      <button type="button" className="btn-danger btn-sm" onClick={() => deleteMedia(m.id)} disabled={deletingMedia === m.id}>
                        {deletingMedia === m.id && <Spinner className="h-3 w-3" />} Delete
                      </button>
                      <button type="button" className="btn-ghost btn-sm" onClick={() => setConfirmMedia(null)}>
                        Keep
                      </button>
                    </span>
                  ) : (
                    <button type="button" className="btn-icon hover:text-destructive" title="Delete file" onClick={() => setConfirmMedia(m.id)}>
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}

        <Field label="Add photos or videos">
          <FileDrop files={files} onChange={setFiles} accept={MEDIA_EXT} multiple label="Drag & drop images or videos here …" />
        </Field>
        {progress != null && (
          <div>
            <div className="mb-1 flex justify-between text-xs text-muted-foreground">
              <span>Uploading…</span>
              <span>{progress}%</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted">
              <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
            </div>
          </div>
        )}
      </form>
    </Modal>
  )
}
