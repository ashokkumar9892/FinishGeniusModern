import { useRef, useState } from 'react'
import { FolderOpen, Paperclip, Trash2, UploadCloud } from 'lucide-react'
import clsx from 'clsx'

/**
 * Drag & drop file picker with extension validation. `accept` is a list of extensions
 * like ['jpg','png']; invalid files produce the legacy message:
 *   Invalid extension for file "x.docx". Only "jpg, png, gif, mp4, mov" files are supported.
 */
export function FileDrop({
  files,
  onChange,
  accept,
  multiple = false,
  label = 'Drag & drop files here …',
}: {
  files: File[]
  onChange: (files: File[]) => void
  accept?: string[]
  multiple?: boolean
  label?: string
}) {
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [over, setOver] = useState(false)

  const add = (list: FileList | null) => {
    if (!list) return
    const incoming = Array.from(list)
    if (accept?.length) {
      const bad = incoming.find((f) => !accept.includes((f.name.split('.').pop() ?? '').toLowerCase()))
      if (bad) {
        const shown = accept.filter((e) => e !== 'jpeg').join(', ')
        setError(`Invalid extension for file "${bad.name}". Only "${shown}" files are supported.`)
        return
      }
    }
    setError(null)
    onChange(multiple ? [...files, ...incoming] : incoming.slice(0, 1))
  }

  return (
    <div>
      <div
        onDragOver={(e) => {
          e.preventDefault()
          setOver(true)
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => {
          e.preventDefault()
          setOver(false)
          add(e.dataTransfer.files)
        }}
        onClick={() => input.current?.click()}
        className={clsx(
          'flex flex-col items-center justify-center gap-1 rounded-md border-2 border-dashed px-4 py-6 text-sm text-muted-foreground cursor-pointer transition-colors',
          over ? 'border-primary bg-accent' : 'hover:border-primary/60',
          error && 'border-destructive',
        )}
      >
        <UploadCloud className="h-6 w-6" />
        <span>{label}</span>
        <span className="btn-primary btn-sm mt-2">
          <FolderOpen className="h-3.5 w-3.5" /> Browse …
        </span>
        {accept?.length ? <span className="text-xs">Allowed: {accept.filter((e) => e !== 'jpeg').join(', ')}</span> : null}
        <input
          ref={input}
          type="file"
          className="hidden"
          multiple={multiple}
          accept={accept?.map((e) => '.' + e).join(',')}
          onChange={(e) => {
            add(e.target.files)
            e.target.value = ''
          }}
        />
      </div>
      {error && (
        <div className="mt-2 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive">
          <div className="font-semibold">File Upload Error</div>• {error}
        </div>
      )}
      {files.length > 0 && (
        <ul className="mt-2 space-y-1">
          {files.map((f, i) => (
            <li key={i} className="flex items-center gap-2 rounded border px-2 py-1 text-xs">
              <Paperclip className="h-3.5 w-3.5 text-muted-foreground" />
              <span className="flex-1 truncate">{f.name}</span>
              <span className="text-muted-foreground">{(f.size / 1024).toFixed(0)} KB</span>
              <button type="button" className="text-muted-foreground hover:text-destructive" onClick={() => onChange(files.filter((_, j) => j !== i))} aria-label="Remove file">
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </li>
          ))}
          <li className="text-xs text-muted-foreground">
            {files.length} file{files.length > 1 ? 's' : ''} selected
          </li>
        </ul>
      )}
    </div>
  )
}
