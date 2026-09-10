import { useState } from 'react'
import { File, FileArchive, FileImage, FileSpreadsheet, FileText, FileVideo, Presentation } from 'lucide-react'
import clsx from 'clsx'
import { fileUrl } from '@/lib/api'
import { docKind, extOf, type DocumentRow } from './shared'

function iconFor(ext: string) {
  if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'tif', 'tiff'].includes(ext)) return { Icon: FileImage, tone: 'text-violet-600 bg-violet-50 dark:bg-violet-500/10' }
  if (['mp4', 'mov', 'avi', 'wmv', 'webm', 'mkv'].includes(ext)) return { Icon: FileVideo, tone: 'text-pink-600 bg-pink-50 dark:bg-pink-500/10' }
  if (ext === 'pdf') return { Icon: FileText, tone: 'text-red-600 bg-red-50 dark:bg-red-500/10' }
  if (['doc', 'docx', 'rtf', 'txt', 'odt'].includes(ext)) return { Icon: FileText, tone: 'text-blue-600 bg-blue-50 dark:bg-blue-500/10' }
  if (['xls', 'xlsx', 'csv', 'ods'].includes(ext)) return { Icon: FileSpreadsheet, tone: 'text-emerald-600 bg-emerald-50 dark:bg-emerald-500/10' }
  if (['ppt', 'pptx', 'odp'].includes(ext)) return { Icon: Presentation, tone: 'text-orange-600 bg-orange-50 dark:bg-orange-500/10' }
  if (['zip', 'rar', '7z', 'gz', 'tar'].includes(ext)) return { Icon: FileArchive, tone: 'text-amber-700 bg-amber-50 dark:bg-amber-500/10' }
  return { Icon: File, tone: 'text-slate-500 bg-slate-100 dark:bg-slate-500/10' }
}

/** Coloured file-type tile ("PDF", "XLSX"…) used when there is no image thumbnail. */
export function FileTypeTile({ name, className }: { name?: string | null; className?: string }) {
  const ext = extOf(name)
  const { Icon, tone } = iconFor(ext)
  return (
    <span className={clsx('flex flex-col items-center justify-center rounded-md border shrink-0', tone, className ?? 'h-12 w-16')}>
      <Icon className="h-5 w-5" />
      {ext && <span className="mt-0.5 text-[9px] font-bold uppercase tracking-wide leading-none">{ext.slice(0, 5)}</span>}
    </span>
  )
}

/** Image thumbnail (lazy loaded) or a file-type tile; clickable when `onClick` is given. */
export function DocThumb({ doc, onClick, className }: { doc: DocumentRow; onClick?: () => void; className?: string }) {
  const [broken, setBroken] = useState(false)
  const size = className ?? 'h-12 w-16'
  const body =
    docKind(doc) === 'image' && doc.storedFile && !broken ? (
      <img src={fileUrl(doc.storedFile)} alt="" loading="lazy" onError={() => setBroken(true)} className={clsx('rounded-md border object-cover bg-muted shrink-0', size)} />
    ) : (
      <FileTypeTile name={doc.fileName || doc.storedFile} className={size} />
    )
  if (!onClick) return body
  return (
    <button type="button" onClick={onClick} className="shrink-0 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring hover:opacity-90" title="Preview">
      {body}
    </button>
  )
}

export function ProgressBar({ value, label }: { value: number; label?: string }) {
  return (
    <div>
      <div className="mb-1 flex justify-between text-xs text-muted-foreground">
        <span>{label ?? (value < 100 ? 'Uploading…' : 'Processing…')}</span>
        <span className="tabular-nums">{value}%</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={100}>
        <div className="h-full bg-primary transition-[width] duration-200" style={{ width: `${value}%` }} />
      </div>
    </div>
  )
}
