import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { ChevronLeft, ChevronRight, Download, FileText, X } from 'lucide-react'
import clsx from 'clsx'
import { fileUrl } from '@/lib/api'
import { docKind, formatBytes, type DocumentRow } from './shared'

/**
 * "Fullscreen View" (legacy FullscreenDocs carousel): every linked document one after the other — PDFs embedded, images and
 * videos shown, Office / text files as a "no preview" card with a download button. Arrow keys / buttons move between
 * documents, Escape closes (only the viewer; the modal underneath stays open).
 */
export function DocumentSlideshow({ open, docs, start = 0, title, onClose }: {
  open: boolean
  docs: DocumentRow[]
  start?: number
  title?: string
  onClose: () => void
}) {
  const [index, setIndex] = useState(start)
  const count = docs.length
  const current = Math.min(index, Math.max(0, count - 1))

  useEffect(() => {
    if (open) setIndex(Math.min(Math.max(0, start), Math.max(0, count - 1)))
  }, [open, start, count])

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault()
        setIndex((i) => Math.max(0, i - 1))
      } else if (e.key === 'ArrowRight') {
        e.preventDefault()
        setIndex((i) => Math.min(count - 1, i + 1))
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [open, count, onClose])

  if (!open || count === 0) return null
  const d = docs[current]
  const kind = docKind(d)
  const url = fileUrl(d.storedFile)
  const hasPrev = current > 0
  const hasNext = current < count - 1

  return createPortal(
    <div className="fixed inset-0 z-[70] flex flex-col bg-slate-950/95 text-white no-print" role="dialog" aria-modal="true" aria-label={title ?? 'Documents'}>
      <div className="flex items-center gap-3 border-b border-white/10 px-3 py-2 sm:px-5">
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-semibold" title={d.name}>
            {d.name}
          </div>
          <div className="truncate text-xs text-white/60">
            {title ? `${title} · ` : ''}Document {current + 1} of {count}
            {d.fileName ? ` · ${d.fileName}` : ''}
          </div>
        </div>
        {d.storedFile && (
          <a href={url} download={d.fileName || d.name} className="btn btn-sm bg-white/10 text-white hover:bg-white/20">
            <Download className="h-4 w-4" /> <span className="hidden sm:inline">Download</span>
          </a>
        )}
        <button className="btn btn-sm h-8 w-8 bg-white/10 px-0 text-white hover:bg-white/20" onClick={onClose} aria-label="Close fullscreen view" title="Close (Esc)">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="relative flex min-h-0 flex-1 items-center justify-center p-2 sm:px-16 sm:py-6">
        {kind === 'image' && <img key={d.id} src={url} alt={d.name} className="max-h-full max-w-full object-contain shadow-2xl" />}
        {kind === 'pdf' && <embed key={d.id} src={`${url}#toolbar=0&navpanes=0`} type="application/pdf" title={d.name} className="h-full w-full rounded bg-white" />}
        {kind === 'video' && <video key={d.id} src={url} controls className="max-h-full max-w-full rounded shadow-2xl" />}
        {kind === 'none' && (
          <div className="flex max-w-sm flex-col items-center gap-3 rounded-xl bg-white/5 px-8 py-10 text-center ring-1 ring-white/10">
            <FileText className="h-14 w-14 text-white/70" />
            <div className="text-base font-semibold">{d.name}</div>
            <div className="text-sm text-white/70">
              No preview available for this file type{d.fileName ? ` (${d.fileName}, ${formatBytes(d.fileSize)})` : ''}.
            </div>
            {d.storedFile && (
              <a href={url} download={d.fileName || d.name} className="btn-primary mt-1">
                <Download className="h-4 w-4" /> Download
              </a>
            )}
          </div>
        )}

        <button
          className={clsx('absolute left-2 top-1/2 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-white/10 hover:bg-white/25 disabled:opacity-20 sm:left-4')}
          onClick={() => setIndex(current - 1)}
          disabled={!hasPrev}
          aria-label="Previous document"
          title="Previous (←)"
        >
          <ChevronLeft className="h-6 w-6" />
        </button>
        <button
          className="absolute right-2 top-1/2 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-white/10 hover:bg-white/25 disabled:opacity-20 sm:right-4"
          onClick={() => setIndex(current + 1)}
          disabled={!hasNext}
          aria-label="Next document"
          title="Next (→)"
        >
          <ChevronRight className="h-6 w-6" />
        </button>
      </div>

      {count > 1 && (
        <div className="flex flex-wrap items-center justify-center gap-1.5 border-t border-white/10 px-3 py-2">
          {docs.map((x, i) => (
            <button
              key={x.id}
              onClick={() => setIndex(i)}
              aria-label={`Show ${x.name}`}
              aria-current={i === current}
              title={x.name}
              className={clsx('h-2.5 rounded-full transition-all', i === current ? 'w-6 bg-white' : 'w-2.5 bg-white/35 hover:bg-white/60')}
            />
          ))}
        </div>
      )}
    </div>,
    document.body,
  )
}
