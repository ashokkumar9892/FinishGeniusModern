import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { Download, FileQuestion, X } from 'lucide-react'
import type { PreviewSource } from './shared'

/**
 * Fullscreen viewer for images, PDFs and videos ("Fullscreen View"). Sits above any open modal; Escape closes
 * only the viewer (the key event is stopped in the capture phase so the modal underneath stays open).
 */
export function FilePreviewModal({ source, onClose }: { source: PreviewSource | null; onClose: () => void }) {
  useEffect(() => {
    if (!source) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopPropagation()
      onClose()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [source, onClose])

  if (!source) return null
  return createPortal(
    <div className="fixed inset-0 z-[70] flex flex-col bg-slate-950/95 text-white no-print" role="dialog" aria-modal="true" aria-label={`Preview of ${source.name}`}>
      <div className="flex items-center gap-3 border-b border-white/10 px-3 py-2 sm:px-5">
        <div className="min-w-0 flex-1 truncate text-sm font-medium" title={source.name}>
          {source.name}
        </div>
        {source.downloadHref && (
          <a href={source.downloadHref} download={source.downloadName ?? source.name} className="btn btn-sm bg-white/10 text-white hover:bg-white/20">
            <Download className="h-4 w-4" /> <span className="hidden sm:inline">Download</span>
          </a>
        )}
        <button className="btn btn-sm h-8 w-8 px-0 bg-white/10 text-white hover:bg-white/20" onClick={onClose} aria-label="Close preview" title="Close (Esc)">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="flex min-h-0 flex-1 items-center justify-center p-2 sm:p-6" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
        {source.kind === 'image' && <img src={source.src} alt={source.name} className="max-h-full max-w-full object-contain shadow-2xl" />}
        {source.kind === 'pdf' && <iframe src={source.src} title={source.name} className="h-full w-full rounded bg-white" />}
        {source.kind === 'video' && <video src={source.src} controls autoPlay className="max-h-full max-w-full rounded shadow-2xl" />}
        {source.kind === 'none' && (
          <div className="flex flex-col items-center gap-3 text-center text-white/80">
            <FileQuestion className="h-12 w-12" />
            <div className="text-base font-medium text-white">No preview available</div>
            <div className="text-sm">This file type can only be downloaded.</div>
            {source.downloadHref && (
              <a href={source.downloadHref} download={source.downloadName ?? source.name} className="btn-primary mt-2">
                <Download className="h-4 w-4" /> Download Only
              </a>
            )}
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}
