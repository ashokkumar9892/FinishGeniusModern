import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { ErrorBanner, LoadingBlock, Modal, SearchInput, Spinner } from '../ui'
import { useToast } from '../toast'
import { linkKey, parseLinkKey, type DocumentRow, type LinkTargets } from './shared'

interface Leaf {
  key: string
  label: string
  sub?: string
}
interface Branch {
  id: string
  label: string
  leaves?: Leaf[]
  children?: Branch[]
}

/** Legacy tree order: Materials → Base, Dye, Pigment, Sundry, Equipment (+ Product when the group has any). */
const MATERIAL_NODES = [
  { type: 1, label: 'Base' },
  { type: 3, label: 'Dye' },
  { type: 2, label: 'Pigment' },
  { type: 5, label: 'Sundry' },
  { type: 4, label: 'Equipment' },
  { type: 7, label: 'Product' },
]
const MAX_LEAVES = 500

function buildTree(t: LinkTargets | undefined): Branch[] {
  if (!t) return []
  return [
    {
      id: 'materials',
      label: 'Materials',
      children: MATERIAL_NODES.map((n) => ({
        id: `materials-${n.type}`,
        label: n.label,
        leaves: t.materials
          .filter((m) => m.materialType === n.type)
          .map((m) => ({ key: linkKey('Material', m.id), label: m.name, sub: m.productCode ?? undefined })),
      })).filter((b) => b.label !== 'Product' || b.leaves.length > 0),
    },
    {
      id: 'formulas',
      label: 'Formulations',
      leaves: t.formulas.map((f) => ({ key: linkKey('Formula', f.id), label: f.name, sub: f.number ? `#${f.number}` : undefined })),
    },
    {
      id: 'steps',
      label: 'Process Steps',
      leaves: t.processSteps.map((s) => ({ key: linkKey('ProcessStep', s.id), label: s.name })),
    },
    {
      id: 'schedules',
      label: 'Process Schedules',
      leaves: t.processSchedules.map((s) => ({ key: linkKey('ProcessSchedule', s.id), label: s.name, sub: s.number ? `#${s.number}` : undefined })),
    },
  ]
}

const allLeaves = (b: Branch): Leaf[] => [...(b.leaves ?? []), ...(b.children ?? []).flatMap(allLeaves)]

function TriCheckbox({ state, onChange, disabled, label }: { state: 'on' | 'off' | 'mixed'; onChange: () => void; disabled?: boolean; label: string }) {
  const ref = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = state === 'mixed'
  }, [state])
  return (
    <input ref={ref} type="checkbox" aria-label={label} className="h-4 w-4 shrink-0 accent-[hsl(var(--primary))]" checked={state === 'on'} disabled={disabled} onChange={onChange} />
  )
}

interface TreeCtx {
  selected: Set<string>
  expanded: Set<string>
  searching: boolean
  matches: (l: Leaf) => boolean
  toggle: (key: string) => void
  setMany: (keys: string[], on: boolean) => void
  toggleExpand: (id: string) => void
}

function BranchRow({ branch, depth, ctx }: { branch: Branch; depth: number; ctx: TreeCtx }) {
  const every = allLeaves(branch)
  const visible = every.filter(ctx.matches)
  if (ctx.searching && visible.length === 0) return null
  const selectedCount = every.filter((l) => ctx.selected.has(l.key)).length
  const visibleSelected = visible.filter((l) => ctx.selected.has(l.key)).length
  const state = visible.length > 0 && visibleSelected === visible.length ? 'on' : visibleSelected > 0 ? 'mixed' : 'off'
  const isOpen = ctx.searching || ctx.expanded.has(branch.id)
  const ownLeaves = (branch.leaves ?? []).filter(ctx.matches)
  const indent = depth * 18

  return (
    <li>
      <div className="flex items-center gap-2 rounded px-1.5 py-1.5 hover:bg-muted/60" style={{ paddingLeft: indent + 6 }}>
        <button type="button" className="text-muted-foreground hover:text-foreground" onClick={() => ctx.toggleExpand(branch.id)} aria-label={isOpen ? `Collapse ${branch.label}` : `Expand ${branch.label}`} aria-expanded={isOpen}>
          {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>
        <TriCheckbox state={state} disabled={visible.length === 0} label={`Select all ${branch.label}`} onChange={() => ctx.setMany(visible.map((l) => l.key), state !== 'on')} />
        <button type="button" className="flex-1 truncate text-left text-sm font-medium" onClick={() => ctx.toggleExpand(branch.id)}>
          {branch.label}
        </button>
        <span className={selectedCount > 0 ? 'badge bg-accent text-accent-foreground' : 'badge bg-muted text-muted-foreground'}>
          {selectedCount}/{every.length}
        </span>
      </div>
      {isOpen && (
        <ul>
          {branch.children?.map((c) => <BranchRow key={c.id} branch={c} depth={depth + 1} ctx={ctx} />)}
          {branch.leaves &&
            (ownLeaves.length === 0 ? (
              <li className="py-1 text-xs text-muted-foreground" style={{ paddingLeft: indent + 50 }}>
                {branch.leaves.length === 0 ? 'Nothing in this group yet.' : 'No matches.'}
              </li>
            ) : (
              <>
                {ownLeaves.slice(0, MAX_LEAVES).map((l) => (
                  <li key={l.key}>
                    <label className="flex cursor-pointer items-center gap-2 rounded px-1.5 py-1 text-sm hover:bg-muted/60" style={{ paddingLeft: indent + 44 }}>
                      <input type="checkbox" className="h-4 w-4 shrink-0 accent-[hsl(var(--primary))]" checked={ctx.selected.has(l.key)} onChange={() => ctx.toggle(l.key)} />
                      <span className="truncate">{l.label}</span>
                      {l.sub && <span className="truncate text-xs text-muted-foreground">{l.sub}</span>}
                    </label>
                  </li>
                ))}
                {ownLeaves.length > MAX_LEAVES && (
                  <li className="py-1 text-xs text-muted-foreground" style={{ paddingLeft: indent + 50 }}>
                    Showing the first {MAX_LEAVES} of {ownLeaves.length} — use the search box to narrow the list.
                  </li>
                )}
              </>
            ))}
        </ul>
      )}
    </li>
  )
}

/** "Link" modal: collapsible checkbox tree of the group's objects; Save replaces the document's links. */
export function LinkDocumentModal({ open, onClose, doc }: { open: boolean; onClose: () => void; doc: DocumentRow | null }) {
  const toast = useToast()
  const qc = useQueryClient()
  const groupId = doc?.groupId ?? 0
  const targets = useQuery({
    queryKey: ['document-link-targets', groupId],
    queryFn: () => api.get<LinkTargets>('/documents/link-targets', { params: { groupId } }).then((r) => r.data),
    enabled: open && groupId > 0,
    staleTime: 30_000,
  })
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [expanded, setExpanded] = useState<Set<string>>(new Set(['materials']))
  const [q, setQ] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !doc) return
    setSelected(new Set(doc.links.map((l) => linkKey(l.entityType, l.entityId))))
    setQ('')
    setError(null)
    // Expand the branches that already contain links so the user sees them.
    const ex = new Set(['materials'])
    for (const l of doc.links) {
      if (l.entityType === 'Formula') ex.add('formulas')
      if (l.entityType === 'ProcessStep') ex.add('steps')
      if (l.entityType === 'ProcessSchedule') ex.add('schedules')
    }
    setExpanded(ex)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, doc?.id])

  const tree = useMemo(() => buildTree(targets.data), [targets.data])
  const known = useMemo(() => new Set(tree.flatMap(allLeaves).map((l) => l.key)), [tree])
  // Links the tree does not show (group-level Pricing / Material Quantities, deleted or archived objects) are kept.
  const others = targets.data ? (doc?.links ?? []).filter((l) => !known.has(linkKey(l.entityType, l.entityId))) : []
  const shownSelected = [...selected].filter((k) => known.has(k)).length

  const needle = q.trim().toLowerCase()
  const ctx: TreeCtx = {
    selected,
    expanded,
    searching: needle.length > 0,
    matches: (l) => !needle || `${l.label} ${l.sub ?? ''}`.toLowerCase().includes(needle),
    toggle: (key) =>
      setSelected((s) => {
        const n = new Set(s)
        if (n.has(key)) n.delete(key)
        else n.add(key)
        return n
      }),
    setMany: (keys, on) =>
      setSelected((s) => {
        const n = new Set(s)
        keys.forEach((k) => (on ? n.add(k) : n.delete(k)))
        return n
      }),
    toggleExpand: (id) =>
      setExpanded((s) => {
        const n = new Set(s)
        if (n.has(id)) n.delete(id)
        else n.add(id)
        return n
      }),
  }

  const save = useMutation({
    mutationFn: () => api.put<{ message: string }>(`/documents/${doc!.id}/links`, [...selected].map(parseLinkKey)),
    onSuccess: (res) => {
      toast.success(res.data.message)
      qc.invalidateQueries({ queryKey: ['documents'] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  return (
    <Modal
      open={open}
      onClose={() => !save.isPending && onClose()}
      title={doc ? `Link — ${doc.name}` : 'Link'}
      size="lg"
      footer={
        <>
          <span className="mr-auto text-xs text-muted-foreground">
            {shownSelected} object{shownSelected === 1 ? '' : 's'} selected
          </span>
          <button className="btn-secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </button>
          <button className="btn-primary" onClick={() => save.mutate()} disabled={save.isPending || !targets.data}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      <p className="mb-3 text-sm">Please select the Objects you wish to link to this Document to.</p>
      <ErrorBanner message={error ?? (targets.isError ? errorMessage(targets.error) : null)} />
      <SearchInput value={q} onChange={setQ} placeholder="Search materials, formulations, steps, schedules…" className="mb-2" />
      {targets.isLoading ? (
        <LoadingBlock label="Loading objects…" />
      ) : (
        <ul className="max-h-[50vh] overflow-y-auto rounded-md border p-1" role="tree">
          {tree.map((b) => (
            <BranchRow key={b.id} branch={b} depth={0} ctx={ctx} />
          ))}
          {ctx.searching && tree.every((b) => allLeaves(b).filter(ctx.matches).length === 0) && (
            <li className="px-3 py-6 text-center text-sm text-muted-foreground">No objects match “{q}”.</li>
          )}
        </ul>
      )}
      {others.length > 0 && (
        <div className="mt-3 text-xs text-muted-foreground">
          <div className="mb-1 font-medium text-foreground/80">Also linked (kept when you save):</div>
          <div className="flex flex-wrap gap-1">
            {others.map((l) => (
              <span key={linkKey(l.entityType, l.entityId)} className="badge bg-muted text-muted-foreground">
                {l.label}
              </span>
            ))}
          </div>
        </div>
      )}
    </Modal>
  )
}
