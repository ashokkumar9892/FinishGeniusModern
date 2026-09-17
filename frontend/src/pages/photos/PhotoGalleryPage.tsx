import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Filter, ImageOff, Images, Pencil, Plus, RotateCcw, Search, Tag, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { date } from '@/lib/format'
import { ConfirmDialog, EmptyState, ErrorBanner, Field, LoadingBlock, Modal, PageHeader, Spinner } from '@/components/ui'
import { FileDrop } from '@/components/FileDrop'
import { useToast } from '@/components/toast'
import { Lightbox } from './Lightbox'
import { TagInput, type TagCount } from './TagInput'

interface PhotoRow {
  id: number
  groupId: number
  name: string
  storedFile: string
  contentType?: string | null
  tags: string[]
  createdAt: string
  createdByName?: string | null
}

/** Matches FileStorage.ImageExtensions on the server so both sides show the same error text. */
const PHOTO_EXT = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp']

interface Filters {
  tags: string[]
  search: string
}
const noFilters: Filters = { tags: [], search: '' }

export default function PhotoGalleryPage() {
  const { groupId } = useGroup()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()

  const [draft, setDraft] = useState<Filters>(noFilters)
  const [applied, setApplied] = useState<Filters>(noFilters)
  const [lastGroup, setLastGroup] = useState(groupId)
  if (lastGroup !== groupId) {
    // Filters belong to a group's tags; start clean when the header group changes.
    setLastGroup(groupId)
    setDraft(noFilters)
    setApplied(noFilters)
  }

  const [uploading, setUploading] = useState(false)
  const [editing, setEditing] = useState<PhotoRow | null>(null)
  const [deleting, setDeleting] = useState<PhotoRow | null>(null)
  const [viewIndex, setViewIndex] = useState<number | null>(null)

  const photos = useQuery({
    queryKey: ['photos', groupId, applied.tags.join(','), applied.search],
    queryFn: () =>
      api
        .get<PhotoRow[]>('/photos', { params: { groupId, search: applied.search || undefined, tags: applied.tags.join(',') || undefined } })
        .then((r) => r.data),
    placeholderData: keepPreviousData,
  })
  const tags = useQuery({
    queryKey: ['photo-tags', groupId],
    queryFn: () => api.get<TagCount[]>('/photos/tags', { params: { groupId } }).then((r) => r.data),
  })

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['photos', groupId] })
    qc.invalidateQueries({ queryKey: ['photo-tags', groupId] })
  }

  const del = useMutation({
    mutationFn: (id: number) => api.delete<{ message: string }>(`/photos/${id}`),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      setViewIndex(null)
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const list = photos.data ?? []
  const filtered = applied.tags.length > 0 || applied.search !== ''
  const dirty = draft.search.trim() !== applied.search || draft.tags.join('') !== applied.tags.join('')

  const apply = (e?: FormEvent) => {
    e?.preventDefault()
    setApplied({ tags: draft.tags, search: draft.search.trim() })
  }
  const clear = () => {
    setDraft(noFilters)
    setApplied(noFilters)
  }
  const filterByTag = (tag: string) => {
    if (applied.tags.some((t) => t.toLowerCase() === tag.toLowerCase())) return
    const next = { tags: [...applied.tags, tag], search: applied.search }
    setDraft({ ...next, search: draft.search })
    setApplied(next)
    setViewIndex(null)
  }

  const lightboxItems = useMemo(
    () =>
      list.map((p) => ({
        src: fileUrl(p.storedFile),
        title: p.name,
        caption: (
          <span className="inline-flex flex-wrap items-center justify-center gap-1.5">
            {p.tags.map((t) => (
              <span key={t} className="badge bg-white/10 text-white">
                {t}
              </span>
            ))}
            <span className="text-white/50">
              {date(p.createdAt)}
              {p.createdByName && ` · ${p.createdByName}`}
            </span>
          </span>
        ),
      })),
    [list],
  )

  return (
    <>
      <PageHeader
        title="Photo Gallery"
        breadcrumbs={['Photo Gallery']}
        actions={
          <>
            <button className="btn-secondary" onClick={() => navigate(-1)}>
              <ArrowLeft className="h-4 w-4" /> Go back
            </button>
            <button className="btn-primary" onClick={() => setUploading(true)}>
              <Plus className="h-4 w-4" /> New Photo
            </button>
          </>
        }
      />

      <form onSubmit={apply} className="card mb-5 p-3">
        <div className="grid gap-2 md:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_auto]">
          <TagInput
            value={draft.tags}
            onChange={(t) => setDraft((d) => ({ ...d, tags: t }))}
            suggestions={tags.data ?? []}
            placeholder="Filters by tags..."
            allowNew={false}
          />
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <input
              className="input pl-8"
              placeholder="Search by name..."
              value={draft.search}
              onChange={(e) => setDraft((d) => ({ ...d, search: e.target.value }))}
            />
          </div>
          <div className="flex gap-2">
            <button type="submit" className={clsx('flex-1 md:flex-none', dirty ? 'btn-primary' : 'btn-secondary')}>
              <Filter className="h-4 w-4" /> Apply tags/search
            </button>
            <button type="button" className="btn-ghost flex-1 md:flex-none" onClick={clear} disabled={!filtered && !dirty}>
              <RotateCcw className="h-4 w-4" /> Clear filters
            </button>
          </div>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
          <span>
            {photos.isLoading ? 'Loading photos…' : `${list.length} photo${list.length === 1 ? '' : 's'}`}
            {filtered && ' matching'}
          </span>
          {photos.isFetching && !photos.isLoading && <Spinner className="h-3 w-3" />}
          {filtered && applied.tags.length > 0 && <span>Tags: {applied.tags.join(' + ')}</span>}
          {filtered && applied.search && <span>Search: “{applied.search}”</span>}
        </div>
      </form>

      {photos.isError && <ErrorBanner message={errorMessage(photos.error)} />}

      {photos.isLoading ? (
        <LoadingBlock label="Loading photos…" />
      ) : list.length === 0 ? (
        <div className="card">
          {filtered ? (
            <EmptyState
              title="No photos match these filters"
              description="Try removing a tag or changing the search text."
              icon={<Filter className="h-5 w-5" />}
              action={
                <button className="btn-secondary" onClick={clear}>
                  <RotateCcw className="h-4 w-4" /> Clear filters
                </button>
              }
            />
          ) : (
            <EmptyState
              title="No photos yet"
              description="Upload photos of parts, finishes and defects, and tag them so the team can find them later."
              icon={<Images className="h-5 w-5" />}
              action={
                <button className="btn-primary" onClick={() => setUploading(true)}>
                  <Plus className="h-4 w-4" /> New Photo
                </button>
              }
            />
          )}
        </div>
      ) : (
        <div className="columns-1 gap-4 sm:columns-2 lg:columns-3 2xl:columns-4">
          {list.map((p, i) => (
            <PhotoCard
              key={p.id}
              photo={p}
              activeTags={applied.tags}
              onOpen={() => setViewIndex(i)}
              onEdit={() => setEditing(p)}
              onDelete={() => setDeleting(p)}
              onTag={filterByTag}
            />
          ))}
        </div>
      )}

      <Lightbox items={lightboxItems} index={viewIndex} onIndex={setViewIndex} onClose={() => setViewIndex(null)} />

      {uploading && (
        <UploadModal
          groupId={groupId}
          suggestions={tags.data ?? []}
          onClose={() => setUploading(false)}
          onDone={() => {
            setUploading(false)
            refresh()
          }}
        />
      )}
      {editing && (
        <EditModal
          photo={editing}
          suggestions={tags.data ?? []}
          onClose={() => setEditing(null)}
          onDone={() => {
            setEditing(null)
            refresh()
          }}
        />
      )}
      <ConfirmDialog
        open={!!deleting}
        title="Delete photo?"
        message={`Are you sure you want to delete the "${deleting?.name ?? ''}" photo? This cannot be undone.`}
        busy={del.isPending}
        onConfirm={() => deleting && del.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

function PhotoCard({ photo, activeTags, onOpen, onEdit, onDelete, onTag }: {
  photo: PhotoRow
  activeTags: string[]
  onOpen: () => void
  onEdit: () => void
  onDelete: () => void
  onTag: (tag: string) => void
}) {
  const [broken, setBroken] = useState(false)
  const active = new Set(activeTags.map((t) => t.toLowerCase()))
  return (
    <figure className="group card relative mb-4 break-inside-avoid overflow-hidden">
      <button type="button" className="block w-full overflow-hidden bg-muted" onClick={onOpen} disabled={broken} aria-label={`View ${photo.name}`}>
        {broken ? (
          <div className="flex h-40 flex-col items-center justify-center gap-1 text-xs text-muted-foreground">
            <ImageOff className="h-6 w-6" /> Image unavailable
          </div>
        ) : (
          <img
            src={fileUrl(photo.storedFile, false, null, 600)} // a card is ~300px wide; the full picture opens in the lightbox
            alt={photo.name}
            loading="lazy"
            decoding="async"
            onError={() => setBroken(true)}
            className="h-auto w-full transition-transform duration-300 group-hover:scale-[1.02]"
          />
        )}
      </button>
      <div className="absolute right-2 top-2 flex gap-1 transition-opacity focus-within:opacity-100 sm:opacity-0 sm:group-hover:opacity-100">
        <button type="button" className="btn-icon bg-white/95 text-slate-700 shadow hover:bg-white hover:text-slate-900" title="Edit" onClick={onEdit}>
          <Pencil className="h-4 w-4" />
        </button>
        <button type="button" className="btn-icon bg-white/95 text-slate-700 shadow hover:bg-white hover:text-destructive" title="Delete" onClick={onDelete}>
          <Trash2 className="h-4 w-4" />
        </button>
      </div>
      <figcaption className="p-3">
        <div className="truncate text-sm font-medium" title={photo.name}>
          {photo.name}
        </div>
        {photo.tags.length > 0 && (
          <div className="mt-1.5 flex flex-wrap gap-1">
            {photo.tags.map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => onTag(t)}
                title={`Filter by "${t}"`}
                className={clsx(
                  'badge gap-1 transition-colors',
                  active.has(t.toLowerCase()) ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-primary/10 hover:text-primary',
                )}
              >
                <Tag className="h-3 w-3" />
                {t}
              </button>
            ))}
          </div>
        )}
        <div className="mt-1.5 text-[11px] text-muted-foreground">
          {date(photo.createdAt)}
          {photo.createdByName && ` · ${photo.createdByName}`}
        </div>
      </figcaption>
    </figure>
  )
}

function UploadModal({ groupId, suggestions, onClose, onDone }: {
  groupId: number
  suggestions: TagCount[]
  onClose: () => void
  onDone: () => void
}) {
  const toast = useToast()
  const [files, setFiles] = useState<File[]>([])
  const [name, setName] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  const previews = useMemo(() => files.map((f) => ({ key: `${f.name}-${f.size}-${f.lastModified}`, url: URL.createObjectURL(f), name: f.name })), [files])
  useEffect(() => () => previews.forEach((p) => URL.revokeObjectURL(p.url)), [previews])

  const save = useMutation({
    mutationFn: () => {
      const fd = new FormData()
      fd.append('groupId', String(groupId))
      files.forEach((f) => fd.append('files', f))
      if (files.length === 1 && name.trim()) fd.append('name', name.trim())
      if (tags.length) fd.append('tags', tags.join(','))
      return api.post<{ message: string }>('/photos', fd, {
        onUploadProgress: (e) => setProgress(e.total ? Math.round((e.loaded * 100) / e.total) : null),
      })
    },
    onSuccess: (res) => {
      toast.success(res.data.message)
      onDone()
    },
    onError: (e) => setError(errorMessage(e)),
    onSettled: () => setProgress(null),
  })

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    setError(null)
    if (!files.length) {
      setError('Please select at least one photo to upload.')
      return
    }
    save.mutate()
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="New Photo"
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form="photo-upload" disabled={save.isPending}>
            {save.isPending && <Spinner />} {files.length > 1 ? `Upload ${files.length} Photos` : 'Save'}
          </button>
        </>
      }
    >
      <form id="photo-upload" onSubmit={submit} className="space-y-4">
        <ErrorBanner message={error} />
        <FileDrop files={files} onChange={setFiles} accept={PHOTO_EXT} multiple label="Drag & drop photos here …" />
        {previews.length > 0 && (
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
            {previews.map((p) => (
              <img key={p.key} src={p.url} alt={p.name} title={p.name} className="aspect-square w-full rounded-md border object-cover" />
            ))}
          </div>
        )}
        {files.length <= 1 && (
          <Field label="Name" hint="Defaults to the file name.">
            <input className="input" value={name} maxLength={400} onChange={(e) => setName(e.target.value)} placeholder={files[0]?.name.replace(/\.[^.]+$/, '') ?? 'Photo name'} />
          </Field>
        )}
        {files.length > 1 && <p className="text-xs text-muted-foreground">Each photo is named after its file; rename them afterwards if needed.</p>}
        <Field label="Tags" hint="Press Enter or comma to add a tag. Tags apply to every uploaded photo.">
          <TagInput value={tags} onChange={setTags} suggestions={suggestions} />
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

function EditModal({ photo, suggestions, onClose, onDone }: {
  photo: PhotoRow
  suggestions: TagCount[]
  onClose: () => void
  onDone: () => void
}) {
  const toast = useToast()
  const [name, setName] = useState(photo.name)
  const [tags, setTags] = useState<string[]>(photo.tags)
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () => api.put<{ message: string }>(`/photos/${photo.id}`, { name, tags }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      onDone()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    setError(null)
    if (!name.trim()) {
      setError('Name is required.')
      return
    }
    save.mutate()
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Edit Photo"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" type="submit" form="photo-edit" disabled={save.isPending}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <form id="photo-edit" onSubmit={submit} className="space-y-4">
        <ErrorBanner message={error} />
        <img src={fileUrl(photo.storedFile, false, null, 400)} alt={photo.name} className="mx-auto max-h-56 rounded-md border object-contain" />
        <Field label="Name" required>
          <input className="input" value={name} maxLength={400} onChange={(e) => setName(e.target.value)} autoFocus />
        </Field>
        <Field label="Tags" hint="Press Enter or comma to add a tag.">
          <TagInput value={tags} onChange={setTags} suggestions={suggestions} />
        </Field>
      </form>
    </Modal>
  )
}
