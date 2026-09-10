import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { dateTime, num } from '@/lib/format'
import { ErrorBanner, LoadingBlock, Modal } from '@/components/ui'
import { PrintPortal } from './shared'
import type { SchedulePrint } from './types'

const range = (min?: number | null, max?: number | null) =>
  min == null && max == null ? '' : `${min == null ? '—' : num(min, 4)} – ${max == null ? '—' : num(max, 4)}`

/** Paper-style printable schedule: header + every step, sub step pass and value (schedule edits applied). */
export function SchedulePrintDocument({ data }: { data: SchedulePrint }) {
  return (
    <div className="bg-white text-slate-900 text-[12px] leading-snug">
      <header className="flex items-start justify-between gap-4 border-b-2 border-orange-500 pb-2 mb-3">
        <div>
          <div className="text-[10px] font-semibold uppercase tracking-[0.2em] text-orange-600">Finish Genius · Process Schedule</div>
          <h1 className="text-xl font-bold">{data.name}</h1>
          <div className="text-slate-600">
            Schedule # <b className="text-slate-900">{data.number}</b>
            {data.customerName && <> · Customer <b className="text-slate-900">{data.customerName}</b></>}
          </div>
        </div>
        <div className="text-right text-[11px] text-slate-600">
          <div>{data.groupName}</div>
          {data.departmentName && <div>{data.departmentName}</div>}
          <div>Printed {dateTime(data.printedAt)}</div>
          {data.isArchived && <div className="font-semibold text-red-600">ARCHIVED</div>}
        </div>
      </header>
      {data.steps.length === 0 && <p className="text-slate-500">This schedule has no steps.</p>}
      {data.steps.map((s) => (
        <section key={s.scheduleStepId} className="mb-4" style={{ breakInside: 'avoid-page' }}>
          <h2 className="text-[14px] font-bold bg-slate-100 px-2 py-1 rounded">
            #{s.number} {s.name}
            {s.name !== s.originalName && <span className="ml-2 text-[11px] font-normal text-slate-500">(step: {s.originalName})</span>}
          </h2>
          {s.passes.length === 0 ? (
            <p className="px-2 py-1 text-slate-500">No sub steps filled.</p>
          ) : (
            <table className="w-full mt-1 border-collapse">
              <thead>
                <tr className="text-left text-[10px] uppercase tracking-wide text-slate-500">
                  <th className="px-2 py-1 w-14">Sub step</th>
                  <th className="px-2 py-1 w-2/5">Characteristic</th>
                  <th className="px-2 py-1">Value</th>
                  <th className="px-2 py-1 w-28">Range</th>
                </tr>
              </thead>
              <tbody>
                {s.passes.map((p) =>
                  p.values.map((v, i) => (
                    <tr key={v.processStepValueId} className={i === 0 ? 'border-t border-slate-300' : 'border-t border-slate-100'}>
                      <td className="px-2 py-0.5 align-top font-semibold">{i === 0 ? p.label : ''}</td>
                      <td className="px-2 py-0.5 align-top">
                        {i === 0 && <div className="text-[10px] font-semibold text-slate-500">{p.name}</div>}
                        {v.characteristic}
                      </td>
                      <td className="px-2 py-0.5 align-top font-medium whitespace-pre-line">{v.display || '—'}</td>
                      <td className="px-2 py-0.5 align-top text-slate-600">{range(v.minValue, v.maxValue)}</td>
                    </tr>
                  )),
                )}
              </tbody>
            </table>
          )}
        </section>
      ))}
    </div>
  )
}

export function SchedulePrintModal({ scheduleId, open, onClose }: { scheduleId: number | null; open: boolean; onClose: () => void }) {
  const q = useQuery({
    queryKey: ['process-schedule-print', scheduleId],
    queryFn: () => api.get<SchedulePrint>(`/process-schedules/${scheduleId}/print`).then((r) => r.data),
    enabled: open && !!scheduleId,
    staleTime: 0,
  })
  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        size="xl"
        title={`Print Process Schedule${q.data ? ` ${q.data.name}` : ''}`}
        footer={
          <>
            <button className="btn-secondary" onClick={onClose}>Close</button>
            <button className="btn-primary" disabled={!q.data} onClick={() => window.print()}>
              <Printer className="h-4 w-4" /> Print
            </button>
          </>
        }
      >
        {q.isLoading ? (
          <LoadingBlock />
        ) : q.isError ? (
          <ErrorBanner message={errorMessage(q.error)} />
        ) : q.data ? (
          <div className="rounded-md border shadow-inner bg-white p-5">
            <SchedulePrintDocument data={q.data} />
          </div>
        ) : null}
      </Modal>
      {open && q.data && (
        <PrintPortal>
          <SchedulePrintDocument data={q.data} />
        </PrintPortal>
      )}
    </>
  )
}
