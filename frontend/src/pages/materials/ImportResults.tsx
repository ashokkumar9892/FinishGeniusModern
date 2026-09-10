import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react'
import clsx from 'clsx'
import type { ImportResult } from './shared'

/** Summary of a bulk material upload (shared by "Blk Upld" and the Import page). */
export function ImportResults({ result }: { result: ImportResult }) {
  const ok = result.errors.length === 0
  const stats = [
    { label: 'Created', value: result.created, tone: 'text-success' },
    { label: 'Skipped', value: result.skipped, tone: 'text-muted-foreground' },
    { label: 'New categories', value: result.categoriesCreated, tone: 'text-primary' },
    { label: 'Documents linked', value: result.documentsLinked, tone: 'text-primary' },
    { label: 'Errors', value: result.errors.length, tone: result.errors.length ? 'text-destructive' : 'text-muted-foreground' },
  ]
  return (
    <div className="space-y-3">
      <div
        className={clsx(
          'flex items-start gap-2 rounded-md border px-3 py-2 text-sm',
          ok ? 'border-success/30 bg-success/10 text-success' : 'border-amber-300 bg-amber-50 text-amber-900 dark:bg-amber-500/10 dark:text-amber-300',
        )}
      >
        {ok ? <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0" /> : <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />}
        <span className="font-medium">{result.message}</span>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
        {stats.map((s) => (
          <div key={s.label} className="rounded-md border px-3 py-2">
            <div className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">{s.label}</div>
            <div className={clsx('text-xl font-semibold tabular-nums', s.tone)}>{s.value}</div>
          </div>
        ))}
      </div>
      {result.errors.length > 0 && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5">
          <div className="flex items-center gap-2 border-b border-destructive/20 px-3 py-2 text-sm font-semibold text-destructive">
            <XCircle className="h-4 w-4" /> Rows not imported ({result.errors.length})
          </div>
          <ul className="max-h-48 overflow-y-auto px-3 py-2 text-xs space-y-1 text-destructive">
            {result.errors.map((e, i) => (
              <li key={i}>• {e}</li>
            ))}
          </ul>
        </div>
      )}
      {result.warnings.length > 0 && (
        <div className="rounded-md border">
          <div className="flex items-center gap-2 border-b px-3 py-2 text-sm font-semibold text-muted-foreground">
            <Info className="h-4 w-4" /> Notes ({result.warnings.length})
          </div>
          <ul className="max-h-40 overflow-y-auto px-3 py-2 text-xs space-y-1 text-muted-foreground">
            {result.warnings.map((w, i) => (
              <li key={i}>• {w}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
