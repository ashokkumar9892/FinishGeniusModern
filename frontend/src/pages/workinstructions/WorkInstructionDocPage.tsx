import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { AxiosResponse } from 'axios'
import {
  ArrowLeft, CheckCircle2, ChevronDown, Copy, Download, Eye, FileText, History, ListChecks, ListOrdered, Pencil, Play, Plus, Printer, Trash2,
} from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage, fileUrl } from '@/lib/api'
import { date, dateTime, inputDate } from '@/lib/format'
import { Checkbox, ConfirmDialog, EmptyState, Field, LoadingBlock, Modal, Note, PageHeader, Spinner } from '@/components/ui'
import { HistoryModal } from '@/components/HistoryModal'
import { useToast } from '@/components/toast'
import { Lightbox, type LightboxItem } from '@/pages/photos/Lightbox'
import { StatusBadge, type ApiMessage, type WiDoc, type WiItem, type WiItemKind, type WiMedia, type WiRelatedDoc, type WiSignature, type WiStep } from './shared'
import {
  AddButton, Block, ItemModal, ItemsTable, NotApplicable, RelatedDocModal, RelatedDocsTable, SignatureModal, SignaturesTable, type SaveFn,
} from './DocTables'
import { StepModal } from './StepModal'
import { PrintView, waitForImages, type PrintSelection } from './PrintView'

interface HeaderForm {
  documentNumber: string
  name: string
  issueDate: string
  location: string
  controlled: boolean
  purpose: string
  scope: string
  terminology: string
}

const fromDoc = (d: WiDoc): HeaderForm => ({
  documentNumber: d.documentNumber,
  name: d.name,
  issueDate: inputDate(d.issueDate),
  location: d.location ?? '',
  controlled: d.controlled,
  purpose: d.purpose ?? '',
  scope: d.scope ?? '',
  terminology: d.terminology ?? '',
})

type Confirm = { title?: string; message: ReactNode; label?: string; danger?: boolean; run: () => Promise<AxiosResponse<ApiMessage>> }
type Editor =
  | { kind: 'step'; stepId: number | null }
  | { kind: 'related'; row: WiRelatedDoc | null }
  | { kind: 'signature'; row: WiSignature | null }
  | { kind: 'item'; itemKind: WiItemKind; row: WiItem | null }

const SECTIONS = [
  { id: 'wi-page1', label: 'Page 1', sub: 'Header, trail & approvals' },
  { id: 'wi-page2', label: 'Page 2', sub: 'Tools, equipment & materials' },
  { id: 'wi-page3', label: 'Page 3', sub: 'Process steps' },
]

export default function WorkInstructionDocPage() {
  const { id } = useParams()
  const docId = Number(id)
  const [params, setParams] = useSearchParams()
  const edit = params.get('edit') === '1'
  const autoPrint = params.get('print') === '1'
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()

  const query = useQuery({
    queryKey: ['work-instruction', docId],
    queryFn: () => api.get<WiDoc>(`/work-instructions/${docId}`).then((r) => r.data),
    enabled: docId > 0,
  })
  const doc = query.data

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['work-instruction', docId] })
    qc.invalidateQueries({ queryKey: ['work-instructions'] })
  }

  // ---- header form (dirty while `form` is not null) ----
  const [form, setForm] = useState<HeaderForm | null>(null)
  const header = form ?? (doc ? fromDoc(doc) : null)
  const patch = (p: Partial<HeaderForm>) => header && setForm({ ...header, ...p })

  const saveHeader = useMutation({
    mutationFn: () =>
      api.put<ApiMessage>(`/work-instructions/${docId}`, {
        ...header,
        // Only send the date when the user changed it (the stored value carries a time of day).
        issueDate: header && doc && header.issueDate && header.issueDate !== inputDate(doc.issueDate) ? header.issueDate : null,
      }),
    onSuccess: (res) => {
      toast.success(res.data.message)
      setForm(null)
      invalidate()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  // ---- generic actions (release, copy step, deletes…) ----
  const action = useMutation({
    mutationFn: (run: () => Promise<AxiosResponse<ApiMessage>>) => run(),
    onSuccess: (res) => {
      toast.success(res.data.message)
      invalidate()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const [confirm, setConfirm] = useState<Confirm | null>(null)
  const runConfirm = () => {
    if (!confirm) return
    action.mutate(confirm.run, { onSettled: () => setConfirm(null) })
  }

  /** Save used by the page 1/2 modals; throws so the modal can show the server message. */
  const save: SaveFn = async (method, url, body) => {
    const res = await api.request<ApiMessage>({ method, url, data: body })
    toast.success(res.data.message)
    invalidate()
  }

  const [editor, setEditor] = useState<Editor | null>(null)
  const [leaveConfirm, setLeaveConfirm] = useState(false)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [selectOpen, setSelectOpen] = useState(false)
  const [lightbox, setLightbox] = useState<{ items: LightboxItem[]; index: number } | null>(null)
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())

  const setEdit = (on: boolean) =>
    setParams(
      (p) => {
        const n = new URLSearchParams(p)
        if (on) n.set('edit', '1')
        else n.delete('edit')
        return n
      },
      { replace: true },
    )
  const toggleMode = () => {
    if (edit && form) setLeaveConfirm(true)
    else setEdit(!edit)
  }

  // ---- printing ----
  const printRef = useRef<HTMLDivElement>(null)
  const [printSel, setPrintSel] = useState<PrintSelection | null>(null)
  const [printJob, setPrintJob] = useState(0)
  useEffect(() => {
    if (!printJob) return
    let cancelled = false
    waitForImages(printRef.current).then(() => !cancelled && window.print())
    return () => {
      cancelled = true
    }
  }, [printJob])
  useEffect(() => {
    const reset = () => setPrintSel(null)
    window.addEventListener('afterprint', reset)
    return () => window.removeEventListener('afterprint', reset)
  }, [])
  const printAll = () => {
    setPrintSel(null)
    setPrintJob((n) => n + 1)
  }
  const autoPrinted = useRef(false)
  useEffect(() => {
    if (!autoPrint || !doc || autoPrinted.current) return
    autoPrinted.current = true
    setParams(
      (p) => {
        const n = new URLSearchParams(p)
        n.delete('print')
        return n
      },
      { replace: true },
    )
    printAll()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoPrint, doc])

  const jump = (targetId: string, sectionId: string) => {
    setCollapsed((c) => {
      if (!c.has(sectionId)) return c
      const n = new Set(c)
      n.delete(sectionId)
      return n
    })
    setTimeout(() => document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 30)
  }
  const toggleSection = (sid: string) =>
    setCollapsed((c) => {
      const n = new Set(c)
      if (n.has(sid)) n.delete(sid)
      else n.add(sid)
      return n
    })

  const nextLevel = useMemo(() => (doc?.steps.length ? Math.max(...doc.steps.map((s) => s.level)) + 1 : 1), [doc])
  const equipment = doc?.items.filter((i) => i.kind === 'Equipment') ?? []
  const materials = doc?.items.filter((i) => i.kind !== 'Equipment') ?? []

  if (query.isLoading) return <LoadingBlock label="Loading work instruction…" />
  if (query.isError || !doc || !header)
    return (
      <>
        <PageHeader title="Standard Work Instruction" breadcrumbs={[['Work Instructions', '/work-instructions'], 'Standard Work Instruction']} />
        <div className="card">
          <EmptyState
            title="Work instruction unavailable"
            description={query.isError ? errorMessage(query.error) : 'This work instruction was not found.'}
            action={
              <button className="btn-secondary" onClick={() => navigate('/work-instructions')}>
                <ArrowLeft className="h-4 w-4" /> Back to Work Instructions
              </button>
            }
          />
        </div>
      </>
    )

  const stepEditing = editor?.kind === 'step' ? (editor.stepId ? doc.steps.find((s) => s.id === editor.stepId) ?? null : null) : null
  const deleteStep = (s: WiStep) =>
    setConfirm({
      message: `Are you sure you want to delete step ${s.level} "${s.title.length > 60 ? s.title.slice(0, 60) + '…' : s.title}" and its ${s.media.length} file(s)?`,
      run: () => api.delete<ApiMessage>(`/work-instructions/steps/${s.id}`),
    })

  return (
    <>
      <div className="no-print">
        <PageHeader
          title="Standard Work Instruction"
          breadcrumbs={[['Work Instructions', '/work-instructions'], 'Standard Work Instruction']}
          subtitle={
            <span className="inline-flex flex-wrap items-center gap-2">
              <span className="font-medium text-foreground">
                #{doc.documentNumber} {doc.name}
              </span>
              <StatusBadge status={doc.status} />
              {doc.groupName && <span className="text-xs">· {doc.groupName}</span>}
            </span>
          }
          actions={
            <>
              <button className="btn-secondary" onClick={() => navigate('/work-instructions')}>
                <ArrowLeft className="h-4 w-4" /> Back
              </button>
              <button className={edit ? 'btn-secondary' : 'btn-primary'} onClick={toggleMode}>
                {edit ? <Eye className="h-4 w-4" /> : <Pencil className="h-4 w-4" />} {edit ? 'View' : 'Edit'}
              </button>
              {!doc.isReleased && (
                <button
                  className="btn-success"
                  disabled={!!form}
                  title={form ? 'Save or discard your changes first' : 'Release this draft'}
                  onClick={() =>
                    setConfirm({
                      title: 'Release Work Instruction',
                      message: `Release "#${doc.documentNumber} ${doc.name}" as RELEASED v${doc.version}? Any later edit will start DRAFT v${doc.version + 1}.`,
                      label: 'Release',
                      danger: false,
                      run: () => api.post<ApiMessage>(`/work-instructions/${docId}/release`),
                    })
                  }
                >
                  <CheckCircle2 className="h-4 w-4" /> Release
                </button>
              )}
              <button className="btn-secondary" onClick={printAll}>
                <Printer className="h-4 w-4" /> Print All Slides
              </button>
              <button className="btn-secondary" onClick={() => setSelectOpen(true)} disabled={!doc.steps.length}>
                <ListChecks className="h-4 w-4" /> Select Slides to Print
              </button>
              <button className="btn-icon" title="History" onClick={() => setHistoryOpen(true)}>
                <History className="h-4 w-4" />
              </button>
            </>
          }
        />

        {edit && doc.isReleased && (
          <div className="mb-4">
            <Note tone="info">
              This document is <b>{doc.status}</b>. Saving any change starts <b>DRAFT v{doc.version + 1}</b> and is recorded in the Edit Trail.
            </Note>
          </div>
        )}

        <div className="grid gap-5 lg:grid-cols-[220px_minmax(0,1fr)]">
          {/* ---- Document Specs jump menu ---- */}
          <nav className="card p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-7rem)] lg:self-start lg:overflow-y-auto" aria-label="Document Specs">
            <div className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              <FileText className="h-3.5 w-3.5" /> Document Specs
            </div>
            <ul className="flex gap-1 overflow-x-auto lg:block lg:space-y-0.5">
              {SECTIONS.map((s) => (
                <li key={s.id} className="shrink-0">
                  <button type="button" onClick={() => jump(s.id, s.id)} className="w-full rounded-md px-2.5 py-1.5 text-left hover:bg-muted">
                    <span className="block text-sm font-medium">{s.label}</span>
                    <span className="hidden text-xs text-muted-foreground lg:block">{s.sub}</span>
                  </button>
                </li>
              ))}
            </ul>
            {doc.steps.length > 0 && (
              <ul className="mt-2 hidden space-y-0.5 border-t pt-2 lg:block">
                {doc.steps.map((s) => (
                  <li key={s.id}>
                    <button type="button" onClick={() => jump(`wi-step-${s.id}`, 'wi-page3')} className="flex w-full items-start gap-2 rounded-md px-2 py-1 text-left text-xs hover:bg-muted">
                      <span className="mt-px grid h-4 min-w-4 place-items-center rounded-full bg-sky-100 px-1 text-[10px] font-bold text-sky-700 dark:bg-sky-500/20 dark:text-sky-200">{s.level}</span>
                      <span className="line-clamp-2 min-w-0">{s.title}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </nav>

          <div className="min-w-0 space-y-5">
            {/* ---------------- Page 1 ---------------- */}
            <Section
              id="wi-page1"
              label="Page 1"
              title={`${header.name || doc.name} Process`}
              open={!collapsed.has('wi-page1')}
              onToggle={() => toggleSection('wi-page1')}
              actions={
                edit && form ? (
                  <>
                    <button className="btn-ghost btn-sm" onClick={() => setForm(null)} disabled={saveHeader.isPending}>
                      Discard
                    </button>
                    <button className="btn-primary btn-sm" onClick={() => saveHeader.mutate()} disabled={saveHeader.isPending}>
                      {saveHeader.isPending && <Spinner className="h-3 w-3" />} Save Changes
                    </button>
                  </>
                ) : null
              }
            >
              <div className="overflow-hidden rounded-md border">
                <div className="grid divide-y sm:grid-cols-3 sm:divide-x sm:divide-y-0">
                  <InfoCell label="Document #">
                    {edit ? (
                      <input className="input" value={header.documentNumber} maxLength={400} onChange={(e) => patch({ documentNumber: e.target.value })} />
                    ) : (
                      doc.documentNumber
                    )}
                  </InfoCell>
                  <InfoCell label="Issue Date">
                    {edit ? <input className="input" type="date" value={header.issueDate} onChange={(e) => patch({ issueDate: e.target.value })} /> : date(doc.issueDate) || '—'}
                  </InfoCell>
                  <InfoCell label="Revision #">
                    <StatusBadge status={doc.status} className="text-sm" />
                  </InfoCell>
                </div>
              </div>

              {edit && (
                <Field label="Document Name" required>
                  <input className="input" value={header.name} maxLength={400} onChange={(e) => patch({ name: e.target.value })} />
                </Field>
              )}

              <div className="grid gap-4 md:grid-cols-2">
                <Field label="Location">
                  {edit ? (
                    <input className="input" value={header.location} onChange={(e) => patch({ location: e.target.value })} placeholder="e.g. Finishing line 2" />
                  ) : (
                    <div className="text-sm">{doc.location || <span className="text-muted-foreground">N/A</span>}</div>
                  )}
                </Field>
                <div>
                  <div className="label">Copy</div>
                  <div className="flex flex-wrap gap-4 pt-1.5" role="radiogroup">
                    {[true, false].map((v) => (
                      <label key={String(v)} className={clsx('inline-flex items-center gap-2 text-sm', edit ? 'cursor-pointer' : 'cursor-default')}>
                        <input
                          type="radio"
                          name="wi-controlled"
                          className="h-4 w-4 accent-[hsl(var(--primary))]"
                          checked={header.controlled === v}
                          disabled={!edit}
                          onChange={() => patch({ controlled: v })}
                        />
                        {v ? 'Controlled Copy' : 'Uncontrolled Copy'}
                      </label>
                    ))}
                  </div>
                </div>
              </div>

              {(['purpose', 'scope', 'terminology'] as const).map((k) => (
                <Field key={k} label={k[0].toUpperCase() + k.slice(1)}>
                  {edit ? (
                    <textarea className="input" rows={3} maxLength={4000} value={header[k]} onChange={(e) => patch({ [k]: e.target.value })} />
                  ) : (
                    <div className="whitespace-pre-line rounded-md bg-muted/40 px-3 py-2 text-sm">{doc[k] || <span className="text-muted-foreground">N/A</span>}</div>
                  )}
                </Field>
              ))}

              <Block title="Edit Trail">
                <div className="max-h-72 overflow-auto rounded-md border">
                  <table className="w-full text-sm">
                    <thead className="sticky top-0 border-b bg-muted">
                      <tr>
                        <th className="th">Version</th>
                        <th className="th">Author</th>
                        <th className="th">Date</th>
                        <th className="th">Log</th>
                      </tr>
                    </thead>
                    <tbody>
                      {doc.trail.map((t) => (
                        <tr key={t.id} className="border-b last:border-0">
                          <td className="td whitespace-nowrap">
                            <StatusBadge status={t.version} />
                          </td>
                          <td className="td whitespace-nowrap">{t.author}</td>
                          <td className="td whitespace-nowrap text-muted-foreground">{dateTime(t.date)}</td>
                          <td className="td min-w-[16rem] text-muted-foreground">{t.log}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {!doc.trail.length && <div className="p-3 text-center text-sm text-muted-foreground">N/A</div>}
                </div>
              </Block>

              <Block title="Related Documents" action={edit && <AddButton label="Add related document" onClick={() => setEditor({ kind: 'related', row: null })} />}>
                <RelatedDocsTable
                  rows={doc.relatedDocuments}
                  edit={edit}
                  onEdit={(row) => setEditor({ kind: 'related', row })}
                  onDelete={(r) =>
                    setConfirm({
                      message: `Are you sure you want to delete the related document "${r.documentName}"?`,
                      run: () => api.delete<ApiMessage>(`/work-instructions/${docId}/related-documents/${r.id}`),
                    })
                  }
                />
              </Block>

              <Block title="Department Approval Signatures" action={edit && <AddButton label="Add signature" onClick={() => setEditor({ kind: 'signature', row: null })} />}>
                <SignaturesTable
                  rows={doc.signatures}
                  edit={edit}
                  onEdit={(row) => setEditor({ kind: 'signature', row })}
                  onDelete={(s) =>
                    setConfirm({
                      message: `Are you sure you want to delete the signature of "${s.name}"?`,
                      run: () => api.delete<ApiMessage>(`/work-instructions/${docId}/signatures/${s.id}`),
                    })
                  }
                />
              </Block>
            </Section>

            {/* ---------------- Page 2 ---------------- */}
            <Section id="wi-page2" label="Page 2" title="Tools, Equipment & Materials" open={!collapsed.has('wi-page2')} onToggle={() => toggleSection('wi-page2')}>
              {(['Equipment', 'Material'] as const).map((kind) => (
                <Block
                  key={kind}
                  title={kind === 'Equipment' ? 'Approved Tools & Equipment' : 'Materials'}
                  action={edit && <AddButton label={kind === 'Equipment' ? 'Add tool / equipment' : 'Add material'} onClick={() => setEditor({ kind: 'item', itemKind: kind, row: null })} />}
                >
                  <ItemsTable
                    rows={kind === 'Equipment' ? equipment : materials}
                    edit={edit}
                    onEdit={(row) => setEditor({ kind: 'item', itemKind: kind, row })}
                    onDelete={(i) =>
                      setConfirm({
                        message: `Are you sure you want to remove "${i.description}"?`,
                        run: () => api.delete<ApiMessage>(`/work-instructions/${docId}/items/${i.id}`),
                      })
                    }
                  />
                </Block>
              ))}
            </Section>

            {/* ---------------- Page 3 ---------------- */}
            <Section
              id="wi-page3"
              label="Page 3"
              title={`${doc.name} Process`}
              open={!collapsed.has('wi-page3')}
              onToggle={() => toggleSection('wi-page3')}
              actions={
                edit ? (
                  <>
                    {doc.steps.length > 1 && (
                      <button
                        className="btn-ghost btn-sm hidden sm:inline-flex"
                        title="Renumber levels 1…N in the current order"
                        disabled={action.isPending}
                        onClick={() => action.mutate(() => api.post<ApiMessage>(`/work-instructions/${docId}/steps/renumber`))}
                      >
                        <ListOrdered className="h-3.5 w-3.5" /> Renumber
                      </button>
                    )}
                    <button className="btn-primary btn-sm" onClick={() => setEditor({ kind: 'step', stepId: null })}>
                      Add New Step <Plus className="h-3.5 w-3.5" />
                    </button>
                  </>
                ) : null
              }
            >
              {doc.steps.length === 0 ? (
                <EmptyState
                  title="No steps yet"
                  description={edit ? 'Add the first step with photos or videos of the operation.' : 'Switch to Edit mode to add steps.'}
                  action={
                    edit ? (
                      <button className="btn-primary" onClick={() => setEditor({ kind: 'step', stepId: null })}>
                        Add New Step <Plus className="h-4 w-4" />
                      </button>
                    ) : (
                      <button className="btn-secondary" onClick={() => setEdit(true)}>
                        <Pencil className="h-4 w-4" /> Edit
                      </button>
                    )
                  }
                />
              ) : (
                <div className="space-y-4">
                  {doc.steps.map((s) => (
                    <StepCard
                      key={s.id}
                      step={s}
                      edit={edit}
                      busy={action.isPending}
                      onEdit={() => setEditor({ kind: 'step', stepId: s.id })}
                      onCopy={() => action.mutate(() => api.post<ApiMessage>(`/work-instructions/steps/${s.id}/copy`))}
                      onDelete={() => deleteStep(s)}
                      onOpenImage={(media) => {
                        const images = s.media.filter((m) => !m.isVideo)
                        setLightbox({
                          items: images.map((m) => ({ src: fileUrl(m.storedFile), title: `Step ${s.level} — ${m.fileName}`, caption: s.title })),
                          index: Math.max(0, images.findIndex((m) => m.id === media.id)),
                        })
                      }}
                    />
                  ))}
                </div>
              )}
            </Section>
          </div>
        </div>

        {edit && form && (
          <div className="sticky bottom-4 z-20 mt-5 flex justify-center">
            <div className="card flex flex-wrap items-center gap-3 px-4 py-2.5 shadow-xl">
              <span className="text-sm font-medium">You have unsaved changes on Page 1.</span>
              <button className="btn-ghost btn-sm" onClick={() => setForm(null)} disabled={saveHeader.isPending}>
                Discard
              </button>
              <button className="btn-primary btn-sm" onClick={() => saveHeader.mutate()} disabled={saveHeader.isPending}>
                {saveHeader.isPending && <Spinner className="h-3 w-3" />} Save Changes
              </button>
            </div>
          </div>
        )}
      </div>

      <PrintView doc={doc} selection={printSel} innerRef={printRef} />

      {/* ---------------- dialogs ---------------- */}
      {editor?.kind === 'step' && (
        <StepModal
          key={editor.stepId ?? 'new'}
          docId={docId}
          step={stepEditing}
          nextLevel={nextLevel}
          onClose={() => setEditor(null)}
          onChanged={invalidate}
          onDeleteStep={(s) => {
            setEditor(null)
            deleteStep(s)
          }}
        />
      )}
      {editor?.kind === 'related' && <RelatedDocModal docId={docId} row={editor.row} onClose={() => setEditor(null)} save={save} />}
      {editor?.kind === 'signature' && <SignatureModal docId={docId} row={editor.row} onClose={() => setEditor(null)} save={save} />}
      {editor?.kind === 'item' && (
        <ItemModal docId={docId} groupId={doc.groupId} kind={editor.itemKind} row={editor.row} onClose={() => setEditor(null)} save={save} />
      )}

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title}
        message={confirm?.message ?? ''}
        confirmLabel={confirm?.label}
        danger={confirm?.danger ?? true}
        busy={action.isPending}
        onConfirm={runConfirm}
        onClose={() => setConfirm(null)}
      />
      <ConfirmDialog
        open={leaveConfirm}
        title="Discard changes?"
        message="You have unsaved changes on Page 1. Discard them and switch to View mode?"
        confirmLabel="Discard"
        onConfirm={() => {
          setForm(null)
          setLeaveConfirm(false)
          setEdit(false)
        }}
        onClose={() => setLeaveConfirm(false)}
      />
      <HistoryModal open={historyOpen} onClose={() => setHistoryOpen(false)} entityType="WorkInstruction" entityId={docId} title={`History — #${doc.documentNumber} ${doc.name}`} />
      {selectOpen && (
        <SelectSlidesModal
          steps={doc.steps}
          onClose={() => setSelectOpen(false)}
          onPrint={(sel) => {
            setSelectOpen(false)
            setPrintSel(sel)
            setPrintJob((n) => n + 1)
          }}
        />
      )}
      <Lightbox
        items={lightbox?.items ?? []}
        index={lightbox?.index ?? null}
        onIndex={(index) => setLightbox((l) => (l ? { ...l, index } : l))}
        onClose={() => setLightbox(null)}
      />
    </>
  )
}

// ---------------------------------------------------------------------------------------------

function Section({ id, label, title, open, onToggle, actions, children }: {
  id: string
  label: string
  title: string
  open: boolean
  onToggle: () => void
  actions?: ReactNode
  children: ReactNode
}) {
  return (
    <section id={id} className="card scroll-mt-4">
      <div className={clsx('flex flex-wrap items-center gap-2 px-4 py-3', open && 'border-b')}>
        <button type="button" className="flex min-w-0 flex-1 items-center gap-2 text-left" onClick={onToggle} aria-expanded={open}>
          <ChevronDown className={clsx('h-4 w-4 shrink-0 text-muted-foreground transition-transform', !open && '-rotate-90')} />
          <span className="min-w-0">
            <span className="block text-[11px] font-semibold uppercase tracking-wider text-primary">{label}</span>
            <span className="block truncate text-base font-semibold">{title}</span>
          </span>
        </button>
        {actions && <div className="flex items-center gap-2">{actions}</div>}
      </div>
      {open && <div className="space-y-5 p-4">{children}</div>}
    </section>
  )
}

function InfoCell({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="px-3 py-2.5">
      <div className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="text-sm font-semibold">{children}</div>
    </div>
  )
}

function StepCard({ step, edit, busy, onEdit, onCopy, onDelete, onOpenImage }: {
  step: WiStep
  edit: boolean
  busy: boolean
  onEdit: () => void
  onCopy: () => void
  onDelete: () => void
  onOpenImage: (m: WiMedia) => void
}) {
  return (
    <article id={`wi-step-${step.id}`} className="scroll-mt-4 overflow-hidden rounded-lg border border-sky-200 dark:border-sky-900">
      <div className="flex items-start gap-3 bg-sky-100/80 px-3 py-2.5 dark:bg-sky-950/60">
        <span className="grid h-8 min-w-8 shrink-0 place-items-center rounded-full bg-white px-2 text-sm font-bold text-sky-700 shadow-sm ring-1 ring-sky-200 dark:bg-sky-900 dark:text-sky-100 dark:ring-sky-800">
          {step.level}
        </span>
        <h4 className="min-w-0 flex-1 whitespace-pre-line break-words pt-1 font-semibold leading-snug">{step.title}</h4>
        {edit && (
          <div className="flex shrink-0 gap-0.5">
            <button className="btn-icon hover:bg-white/70 dark:hover:bg-white/10" title="Edit step" onClick={onEdit}>
              <Pencil className="h-4 w-4" />
            </button>
            <button className="btn-icon hover:bg-white/70 dark:hover:bg-white/10" title="Copy step" onClick={onCopy} disabled={busy}>
              <Copy className="h-4 w-4" />
            </button>
            <button className="btn-icon hover:bg-white/70 hover:text-destructive dark:hover:bg-white/10" title="Delete step" onClick={onDelete}>
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>
      {(step.media.length > 0 || step.body) && (
        <div className="space-y-3 p-3">
          {step.media.length > 0 && (
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
              {step.media.map((m) =>
                m.isVideo ? (
                  <VideoTile key={m.id} media={m} />
                ) : (
                  <button
                    key={m.id}
                    type="button"
                    className="group overflow-hidden rounded-md border bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                    onClick={() => onOpenImage(m)}
                    title={m.fileName}
                  >
                    <img src={fileUrl(m.storedFile, false, null, 600)} alt={m.fileName} loading="lazy" className="aspect-[4/3] w-full object-cover transition-transform duration-300 group-hover:scale-105" />
                  </button>
                ),
              )}
            </div>
          )}
          {step.body && <p className="whitespace-pre-line text-sm leading-relaxed">{step.body}</p>}
        </div>
      )}
    </article>
  )
}

function VideoTile({ media }: { media: WiMedia }) {
  const ref = useRef<HTMLVideoElement>(null)
  const [started, setStarted] = useState(false)
  return (
    <div className="col-span-2 overflow-hidden rounded-md border bg-black">
      <div className="relative">
        {/* #t=0.1 makes browsers render the first frame as a poster */}
        <video ref={ref} src={`${fileUrl(media.storedFile)}#t=0.1`} controls preload="metadata" playsInline className="aspect-video w-full" onPlay={() => setStarted(true)} />
        {!started && (
          <button
            type="button"
            onClick={() => ref.current?.play()}
            className="absolute inset-0 grid place-items-center bg-black/25 transition-colors hover:bg-black/35"
            aria-label={`Play ${media.fileName}`}
          >
            <span className="grid h-14 w-14 place-items-center rounded-full bg-white/90 text-slate-900 shadow-lg">
              <Play className="h-6 w-6 translate-x-0.5 fill-current" />
            </span>
          </button>
        )}
      </div>
      <div className="flex items-center justify-between gap-2 bg-slate-900 px-2 py-1 text-[11px] text-white/80">
        <span className="truncate">{media.fileName}</span>
        <a href={fileUrl(media.storedFile, true)} className="inline-flex shrink-0 items-center gap-1 hover:text-white" title="Download video">
          <Download className="h-3 w-3" />
        </a>
      </div>
    </div>
  )
}

function SelectSlidesModal({ steps, onClose, onPrint }: { steps: WiStep[]; onClose: () => void; onPrint: (sel: PrintSelection) => void }) {
  const [selected, setSelected] = useState<number[]>([])
  const [cover, setCover] = useState(false)
  const toggle = (id: number, on: boolean) => setSelected((s) => (on ? [...s, id] : s.filter((x) => x !== id)))
  const all = selected.length === steps.length

  return (
    <Modal
      open
      onClose={onClose}
      title="Select Slides to Print"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" disabled={!selected.length} onClick={() => onPrint({ stepIds: selected, cover })}>
            <Printer className="h-4 w-4" /> Print {selected.length ? `${selected.length} Slide${selected.length > 1 ? 's' : ''}` : 'Slides'}
          </button>
        </>
      }
    >
      <div className="mb-3 flex items-center justify-between gap-2 border-b pb-3">
        <Checkbox checked={all} onChange={(v) => setSelected(v ? steps.map((s) => s.id) : [])} label={<span className="font-medium">Select all</span>} />
        <Checkbox checked={cover} onChange={setCover} label="Include cover page" />
      </div>
      <ul className="space-y-1">
        {steps.map((s) => (
          <li key={s.id} className="rounded-md px-2 py-1.5 hover:bg-muted/60">
            <Checkbox
              checked={selected.includes(s.id)}
              onChange={(v) => toggle(s.id, v)}
              label={
                <span className="flex items-start gap-2">
                  <span className="mt-px grid h-5 min-w-5 place-items-center rounded-full bg-sky-100 px-1 text-[11px] font-bold text-sky-700 dark:bg-sky-500/20 dark:text-sky-200">{s.level}</span>
                  <span className="line-clamp-2">{s.title}</span>
                  {s.media.length > 0 && <span className="shrink-0 text-xs text-muted-foreground">({s.media.length} file{s.media.length > 1 ? 's' : ''})</span>}
                </span>
              }
            />
          </li>
        ))}
      </ul>
      {!steps.length && <NotApplicable />}
    </Modal>
  )
}
