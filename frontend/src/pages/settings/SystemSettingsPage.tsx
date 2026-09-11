import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, FolderSync, XCircle } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { Card, Checkbox, ErrorBanner, Field, LoadingBlock, Note, PageHeader, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'

interface FolderCheck {
  ok: boolean
  path: string
  message: string
  warning?: string | null
  freeGb?: number | null
}

interface CopyProgress {
  running: boolean
  to?: string | null
  from: string[]
  total: number
  copied: number
  skipped: number
  failed: number
  lastError?: string | null
  startedAt?: string | null
  finishedAt?: string | null
}

interface StorageState {
  root: string
  isDefault: boolean
  defaultRoot: string
  legacyRoot?: string | null
  previousRoots: string[]
  settingsFile: string
  rootCheck: FolderCheck
  legacyCheck: FolderCheck
  copy: CopyProgress
}

function CheckResult({ check }: { check?: FolderCheck | null }) {
  if (!check) return null
  return (
    <div className="mt-2 space-y-2 text-sm">
      <div className={clsx('flex items-start gap-1.5', check.ok ? 'text-success' : 'text-destructive')}>
        {check.ok ? <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0" /> : <XCircle className="h-4 w-4 mt-0.5 shrink-0" />}
        <span>
          {check.message}
          {check.freeGb != null && ` ${check.freeGb} GB free.`}
        </span>
      </div>
      {check.warning && <Note>{check.warning}</Note>}
    </div>
  )
}

const same = (a?: string | null, b?: string | null) => (a ?? '').trim().replace(/[\\/]+$/, '').toLowerCase() === (b ?? '').trim().replace(/[\\/]+$/, '').toLowerCase()

export default function SystemSettingsPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const [root, setRoot] = useState('')
  const [legacyRoot, setLegacyRoot] = useState('')
  const [copyExisting, setCopyExisting] = useState(true)
  const [checks, setChecks] = useState<{ root: FolderCheck; legacyRoot: FolderCheck } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const state = useQuery({
    queryKey: ['storage-settings'],
    queryFn: async () => (await api.get<StorageState>('/settings/storage')).data,
  })
  const copy = useQuery({
    queryKey: ['storage-copy'],
    queryFn: async () => (await api.get<CopyProgress>('/settings/storage/copy')).data,
    refetchInterval: (q) => (q.state.data?.running ? 1500 : false),
  })

  const data = state.data
  useEffect(() => {
    if (!data) return
    setRoot(data.isDefault ? '' : data.root)
    setLegacyRoot(data.legacyRoot ?? '')
  }, [data])

  // When a copy finishes, the folders it emptied are dropped from "earlier folders".
  const wasRunning = useRef(false)
  useEffect(() => {
    const running = !!copy.data?.running
    if (wasRunning.current && !running) qc.invalidateQueries({ queryKey: ['storage-settings'] })
    wasRunning.current = running
  }, [copy.data?.running, qc])

  const test = useMutation({
    mutationFn: async () => (await api.post<{ root: FolderCheck; legacyRoot: FolderCheck }>('/settings/storage/check', { root, legacyRoot })).data,
    onSuccess: (r) => {
      setChecks(r)
      setError(null)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const save = useMutation({
    mutationFn: async () => (await api.put<{ message: string }>('/settings/storage', { root, legacyRoot, copyExisting })).data,
    onSuccess: (r) => {
      toast.success(r.message)
      setChecks(null)
      setError(null)
      qc.invalidateQueries({ queryKey: ['storage-settings'] })
      qc.invalidateQueries({ queryKey: ['storage-copy'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const startCopy = useMutation({
    mutationFn: async () => (await api.post<{ message: string }>('/settings/storage/copy')).data,
    onSuccess: (r) => {
      toast.success(r.message)
      qc.invalidateQueries({ queryKey: ['storage-copy'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (state.isLoading) return <LoadingBlock />
  if (!data) return <ErrorBanner message={errorMessage(state.error)} />

  const rootChanged = !same(root || data.defaultRoot, data.root)
  const legacyChanged = !same(legacyRoot, data.legacyRoot)
  const progress = copy.data
  const busy = test.isPending || save.isPending

  return (
    <>
      <PageHeader title="System Settings" breadcrumbs={['System Settings']} subtitle="Only System Administrators can see and change these settings." />
      <div className="grid gap-5 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card title="File storage">
          <ErrorBanner message={error} />
          <div className="space-y-5">
            <div className="rounded-md border bg-muted/40 px-3 py-2 text-sm">
              <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Files are saved in</div>
              <div className="font-mono break-all">{data.root}</div>
              {data.isDefault && <div className="text-xs text-muted-foreground">Default folder (inside the site).</div>}
              <CheckResult check={data.rootCheck} />
            </div>

            <Field
              label="Storage folder"
              hint={<>Every upload is saved here: documents, photos, work-instruction pictures and videos, logos. Leave empty for the default ({data.defaultRoot}).</>}
            >
              <input className="input font-mono" value={root} placeholder={'D:\\FinishGeniusData'} onChange={(e) => { setRoot(e.target.value); setChecks(null) }} />
              <CheckResult check={checks?.root} />
            </Field>

            {rootChanged && (
              <Checkbox
                checked={copyExisting}
                onChange={setCopyExisting}
                label={<>Copy the files already saved in <span className="font-mono">{data.root}</span> to the new folder</>}
              />
            )}

            <Field
              label="Old site's AppData folder (optional, read only)"
              hint="Old documents, photos and work-instruction pictures are opened from here as they are; nothing is written or deleted in this folder."
            >
              <input className="input font-mono" value={legacyRoot} placeholder={'\\\\oldserver\\FinishGenius\\AppData'} onChange={(e) => { setLegacyRoot(e.target.value); setChecks(null) }} />
              <CheckResult check={checks?.legacyRoot ?? (legacyChanged ? null : data.legacyRoot ? data.legacyCheck : null)} />
            </Field>

            <div className="flex flex-wrap gap-2">
              <button className="btn-secondary" disabled={busy} onClick={() => test.mutate()}>
                {test.isPending && <Spinner />} Test folders
              </button>
              <button className="btn-primary" disabled={busy || (!rootChanged && !legacyChanged)} onClick={() => save.mutate()}>
                {save.isPending && <Spinner />} Save
              </button>
            </div>
          </div>
        </Card>

        <div className="space-y-5">
          <Card title="How it works">
            <ul className="list-disc pl-5 space-y-1.5 text-sm">
              <li>Keep the files in one folder outside the site (for example on the D: drive or a file share). Redeploying the app then never touches them. Back this folder up.</li>
              <li>On IIS the application pool identity needs <b>Modify</b> permission on the storage folder and <b>Read</b> on the old site's folder.</li>
              <li>After changing the folder, files in the earlier folder keep opening until they are copied over; nothing is ever deleted.</li>
              <li>The setting is saved in <span className="font-mono break-all">{data.settingsFile}</span> and takes effect immediately.</li>
            </ul>
          </Card>

          {(data.previousRoots.length > 0 || progress?.startedAt) && (
            <Card
              title="Earlier folders"
              actions={
                data.previousRoots.length > 0 && (
                  <button className="btn-secondary h-8" disabled={progress?.running || startCopy.isPending} onClick={() => startCopy.mutate()}>
                    <FolderSync className="h-4 w-4" /> Copy files now
                  </button>
                )
              }
            >
              {data.previousRoots.length > 0 ? (
                <>
                  <p className="text-sm text-muted-foreground mb-2">Files not copied to the storage folder yet are still opened from:</p>
                  <ul className="text-sm font-mono space-y-1 mb-3">
                    {data.previousRoots.map((p) => <li key={p} className="break-all">{p}</li>)}
                  </ul>
                </>
              ) : (
                <p className="text-sm text-muted-foreground mb-3">All files are in the storage folder.</p>
              )}
              {progress?.startedAt && (
                <div className="rounded-md border px-3 py-2 text-sm space-y-1">
                  <div className="flex items-center gap-2 font-medium">
                    {progress.running && <Spinner />}
                    {progress.running ? 'Copying…' : 'Last copy finished'}
                  </div>
                  <div>
                    {progress.copied} copied, {progress.skipped} already there, {progress.failed} failed
                    {progress.total > 0 && ` — ${progress.copied + progress.skipped + progress.failed} of ${progress.total}`}
                  </div>
                  {progress.lastError && <div className="text-destructive break-all">{progress.lastError}</div>}
                </div>
              )}
            </Card>
          )}
        </div>
      </div>
    </>
  )
}
