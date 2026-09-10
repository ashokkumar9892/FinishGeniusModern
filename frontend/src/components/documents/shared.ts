import { fileUrl } from '@/lib/api'

/** One object a document is linked to (label resolved by the server, e.g. "Dye: Ilva Cherry"). */
export interface DocLink {
  entityType: string
  entityId: number
  label: string
}

/** Row returned by GET /api/documents. */
export interface DocumentRow {
  id: number
  groupId: number
  groupName: string
  name: string
  fileName?: string | null
  storedFile?: string | null
  contentType?: string | null
  fileSize: number
  createdAt: string
  updatedAt?: string | null
  createdByName?: string | null
  links: DocLink[]
}

/** GET /api/documents/link-targets — everything a document can be linked to, in one call. */
export interface LinkTargets {
  materials: { id: number; name: string; productCode?: string | null; materialType: number }[]
  formulas: { id: number; name: string; number?: string | null }[]
  processSteps: { id: number; name: string }[]
  processSchedules: { id: number; name: string; number?: string | null }[]
}

export const MAX_DOCUMENT_BYTES = 100 * 1024 * 1024

export type PreviewKind = 'image' | 'pdf' | 'video' | 'none'

/** What the fullscreen viewer shows. */
export interface PreviewSource {
  src: string
  name: string
  kind: PreviewKind
  downloadHref?: string
  downloadName?: string
}

// Only types the file endpoint serves with a browser-renderable content type.
const IMAGE_EXT = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp']
const VIDEO_EXT = ['mp4', 'mov']

export function extOf(name?: string | null) {
  const m = /\.([a-z0-9]+)$/i.exec(name ?? '')
  return m ? m[1].toLowerCase() : ''
}

export function previewKindOf(fileName?: string | null, contentType?: string | null): PreviewKind {
  const ext = extOf(fileName)
  const ct = (contentType ?? '').toLowerCase()
  if (IMAGE_EXT.includes(ext) || (!ext && ct.startsWith('image/'))) return 'image'
  if (ext === 'pdf' || (!ext && ct === 'application/pdf')) return 'pdf'
  if (VIDEO_EXT.includes(ext) || (!ext && ct.startsWith('video/'))) return 'video'
  return 'none'
}

export const docKind = (d: Pick<DocumentRow, 'fileName' | 'storedFile' | 'contentType'>) =>
  previewKindOf(d.fileName || d.storedFile, d.contentType)

/** Viewer source for a stored document. Downloads use the `download` attribute so the original file name is kept. */
export function docPreview(d: DocumentRow): PreviewSource {
  const url = fileUrl(d.storedFile)
  return { src: url, name: d.name, kind: docKind(d), downloadHref: url, downloadName: d.fileName || d.name }
}

export function formatBytes(bytes?: number | null) {
  const b = Number(bytes ?? 0)
  if (b < 1024) return `${b} B`
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(b < 10 * 1024 ? 1 : 0)} KB`
  return `${(b / (1024 * 1024)).toFixed(1)} MB`
}

export const tooLargeMessage = (f: File) => `File "${f.name}" is ${formatBytes(f.size)}; the maximum size is 100 MB.`

export const linkKey = (entityType: string, entityId: number) => `${entityType}:${entityId}`

export function parseLinkKey(key: string) {
  const i = key.lastIndexOf(':')
  return { entityType: key.slice(0, i), entityId: Number(key.slice(i + 1)) }
}

export const entityTypeLabel: Record<string, string> = {
  Material: 'Material',
  Formula: 'Formula',
  ProcessStep: 'Process Step',
  ProcessSchedule: 'Process Schedule',
  Pricing: 'Pricing',
  MaterialQuantity: 'Material Quantities',
}

export const uploadPercent = (loaded: number, total: number | undefined, fallback: number) =>
  Math.min(100, Math.round((loaded * 100) / (total || fallback || 1)))
