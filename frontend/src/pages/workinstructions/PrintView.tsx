import type { ReactNode, Ref } from 'react'
import { fileUrl } from '@/lib/api'
import { date, dateTime } from '@/lib/format'
import type { WiDoc, WiStep } from './shared'

export interface PrintSelection {
  /** null = every step */
  stepIds: number[] | null
  cover: boolean
}

// Slides print landscape; keep the coloured step bars when printing.
const printCss = `
@media print {
  @page { size: landscape; margin: 10mm; }
  .wi-print, .wi-print * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
}
`

/** Hidden on screen; when printing: a cover page (page 1 + 2 info) and one "slide" per step. */
export function PrintView({ doc, selection, innerRef }: { doc: WiDoc; selection: PrintSelection | null; innerRef: Ref<HTMLDivElement> }) {
  const steps = selection?.stepIds ? doc.steps.filter((s) => selection.stepIds!.includes(s.id)) : doc.steps
  const cover = !selection || selection.cover
  const pages = (cover ? 1 : 0) + steps.length

  return (
    <div ref={innerRef} className="print-only wi-print text-black">
      <style>{printCss}</style>
      {cover && <Cover doc={doc} last={pages === 1} />}
      {steps.map((s, i) => (
        <Slide key={s.id} doc={doc} step={s} last={i === steps.length - 1} />
      ))}
    </div>
  )
}

function SlideHeader({ doc }: { doc: WiDoc }) {
  return (
    <div className="mb-3 flex items-end justify-between border-b-2 border-black pb-1.5">
      <div>
        <div className="text-[10px] font-semibold uppercase tracking-[0.2em]">Standard Work Instruction</div>
        <div className="text-base font-bold">
          #{doc.documentNumber} {doc.name}
        </div>
      </div>
      <div className="text-right text-[10px]">
        <div className="font-semibold">{doc.status}</div>
        <div>{doc.controlled ? 'Controlled Copy' : 'Uncontrolled Copy'}</div>
      </div>
    </div>
  )
}

function Cover({ doc, last }: { doc: WiDoc; last: boolean }) {
  const equipment = doc.items.filter((i) => i.kind === 'Equipment')
  const materials = doc.items.filter((i) => i.kind !== 'Equipment')
  return (
    <section style={{ breakAfter: last ? 'auto' : 'page' }}>
      <SlideHeader doc={doc} />
      <h1 className="mb-3 text-center text-xl font-bold">{doc.name} Process</h1>
      <table className="mb-3 w-full border-collapse text-xs">
        <tbody>
          <tr>
            <Cell label="Document #">{doc.documentNumber}</Cell>
            <Cell label="Issue Date">{date(doc.issueDate)}</Cell>
            <Cell label="Revision #">{doc.status}</Cell>
          </tr>
          <tr>
            <Cell label="Location">{doc.location || 'N/A'}</Cell>
            <Cell label="Copy">{doc.controlled ? 'Controlled Copy' : 'Uncontrolled Copy'}</Cell>
            <Cell label="Group">{doc.groupName}</Cell>
          </tr>
        </tbody>
      </table>
      <div className="grid grid-cols-3 gap-3 text-xs">
        <TextBox title="Purpose" text={doc.purpose} />
        <TextBox title="Scope" text={doc.scope} />
        <TextBox title="Terminology" text={doc.terminology} />
      </div>
      <div className="mt-3 grid grid-cols-2 gap-3 text-xs">
        <PrintList title="Approved Tools & Equipment" items={equipment.map((i) => i.description)} />
        <PrintList title="Materials" items={materials.map((i) => i.description)} />
        <PrintTable title="Related Documents" headers={['Document Number', 'Document Name', 'Author']}
          rows={doc.relatedDocuments.map((r) => [r.documentNumber, r.documentName, r.author ?? ''])} />
        <PrintTable title="Department Approval Signatures" headers={['Name', 'Position', 'Date']}
          rows={doc.signatures.map((s) => [s.name, s.position ?? '', date(s.date)])} />
      </div>
      <div className="mt-3 text-xs">
        <PrintTable title="Edit Trail" headers={['Version', 'Author', 'Date', 'Log']}
          rows={doc.trail.slice(0, 12).map((t) => [t.version, t.author, dateTime(t.date), t.log ?? ''])} />
      </div>
    </section>
  )
}

function Slide({ doc, step, last }: { doc: WiDoc; step: WiStep; last: boolean }) {
  const images = step.media.filter((m) => !m.isVideo)
  const videos = step.media.filter((m) => m.isVideo)
  const imgHeight = images.length <= 1 ? '120mm' : images.length <= 4 ? '58mm' : '40mm'
  return (
    <section style={{ breakAfter: last ? 'auto' : 'page' }}>
      <SlideHeader doc={doc} />
      <div className="mb-3 flex items-start gap-3 rounded border border-sky-300 bg-sky-100 px-3 py-2">
        <span className="grid h-8 min-w-8 place-items-center rounded-full border border-sky-300 bg-white px-2 text-sm font-bold text-sky-800">{step.level}</span>
        <div className="whitespace-pre-line pt-1 text-sm font-bold">{step.title}</div>
      </div>
      {images.length > 0 && (
        <div className={images.length === 1 ? 'flex justify-center' : 'grid grid-cols-2 gap-2'}>
          {images.map((m) => (
            <img key={m.id} src={fileUrl(m.storedFile)} alt={m.fileName} style={{ maxHeight: imgHeight, breakInside: 'avoid' }}
              className="mx-auto w-auto max-w-full rounded border object-contain" />
          ))}
        </div>
      )}
      {videos.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-2 text-xs">
          {videos.map((m) => (
            <span key={m.id} className="rounded border border-dashed px-2 py-1">▶ Video: {m.fileName}</span>
          ))}
        </div>
      )}
      {step.body && <p className="mt-3 whitespace-pre-line text-sm">{step.body}</p>}
    </section>
  )
}

function Cell({ label, children }: { label: string; children: ReactNode }) {
  return (
    <td className="w-1/3 border border-gray-400 px-2 py-1 align-top">
      <div className="text-[9px] font-semibold uppercase text-gray-600">{label}</div>
      <div className="font-semibold">{children}</div>
    </td>
  )
}

function TextBox({ title, text }: { title: string; text?: string | null }) {
  return (
    <div className="rounded border border-gray-400 p-2">
      <div className="mb-1 text-[10px] font-bold uppercase">{title}</div>
      <div className="whitespace-pre-line">{text || 'N/A'}</div>
    </div>
  )
}

function PrintList({ title, items }: { title: string; items: string[] }) {
  return (
    <div>
      <div className="mb-1 text-[10px] font-bold uppercase">{title}</div>
      {items.length ? (
        <ul className="list-disc pl-4">
          {items.map((t, i) => (
            <li key={i}>{t}</li>
          ))}
        </ul>
      ) : (
        <div>N/A</div>
      )}
    </div>
  )
}

function PrintTable({ title, headers, rows }: { title: string; headers: string[]; rows: string[][] }) {
  return (
    <div>
      <div className="mb-1 text-[10px] font-bold uppercase">{title}</div>
      {rows.length ? (
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {headers.map((h) => (
                <th key={h} className="border border-gray-400 bg-gray-100 px-1.5 py-0.5 text-left text-[10px]">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i}>
                {r.map((c, j) => (
                  <td key={j} className="border border-gray-400 px-1.5 py-0.5 align-top">
                    {c}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div>N/A</div>
      )}
    </div>
  )
}

/** Resolves once every <img> inside `root` has loaded (or failed), capped by a timeout. */
export function waitForImages(root: HTMLElement | null, timeout = 6000): Promise<void> {
  const pending = root ? Array.from(root.querySelectorAll('img')).filter((i) => !i.complete) : []
  const loaded = Promise.all(
    pending.map(
      (img) =>
        new Promise<void>((resolve) => {
          img.addEventListener('load', () => resolve(), { once: true })
          img.addEventListener('error', () => resolve(), { once: true })
        }),
    ),
  ).then(() => undefined)
  return Promise.race([loaded, new Promise<void>((resolve) => setTimeout(resolve, pending.length ? timeout : 50))])
}
