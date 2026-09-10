import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { addDays, format, parseISO } from 'date-fns'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { ChevronLeft, ChevronRight, Copy, KeyRound, LineChart as LineChartIcon, RefreshCw } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useLookups } from '@/lib/auth'
import { dateTimeSeconds, num } from '@/lib/format'
import { materialTypeLabel, MaterialType, type Option } from '@/lib/types'
import { SearchSelect } from '@/components/SearchSelect'
import { ConfirmDialog, EmptyState, ErrorBanner, Field, LoadingBlock, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { chartTokens, useIsDark } from './charts'

// ---------------------------------------------------------------------------
// Types (backend: DevicesController)
// ---------------------------------------------------------------------------

export const DeviceTypes = {
  Camera: 1,
  ScaleGrams: 2,
  TempHumiditySensor: 3,
  ScaleKilograms: 4,
  LabelPrinter: 5,
  NetworkBridge: 6,
  DispenseMachine: 7,
} as const

/** Device types that connect through a Network Bridge (mirrors DevicesController.Bridgeable). */
export const BRIDGEABLE: number[] = [DeviceTypes.ScaleGrams, DeviceTypes.ScaleKilograms, DeviceTypes.LabelPrinter]

export interface DeviceRow {
  id: number
  groupId: number
  groupName: string
  name: string
  description?: string | null
  deviceType: number
  deviceTypeLabel: string
  networkBridgeId?: number | null
  networkBridgeName?: string | null
  ipAddress?: string | null
  lastSeenAt?: string | null
  online: boolean
  createdAt: string
}

interface DeviceDetail extends DeviceRow {
  apiKey: string
  canisters: { canisterNo: number; materialId?: number | null; materialName?: string | null }[]
}

interface SaveResponse {
  message: string
  id: number
  deviceType: number
}

interface MaterialRef {
  id: number
  materialType: number
  productName: string
  productCode?: string | null
}

interface MetricSeries {
  name: string
  unit?: string | null
  count: number
  min?: number | null
  max?: number | null
  latest?: number | null
  latestText?: string | null
  points: { timestamp: string; value?: number | null; textValue?: string | null }[]
}

/** Server dates are UTC without "Z". */
export function parseUtc(s: string) {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : `${s}Z`)
}

const IP_RE = /^https?:\/\/\S+$/i

// ---------------------------------------------------------------------------
// Add / edit device
// ---------------------------------------------------------------------------

interface FormState {
  groupId: number
  name: string
  description: string
  deviceType: number | null
  networkBridgeId: number | null
  ipAddress: string
}

export function DeviceFormModal({ deviceId, onClose, onSaved, onOpenCanisters }: {
  deviceId: number | null
  onClose: () => void
  onSaved: (res: SaveResponse, created: boolean) => void
  onOpenCanisters: (id: number) => void
}) {
  const { groupId: currentGroup, groups } = useGroup()
  const lookups = useLookups()
  const qc = useQueryClient()
  const toast = useToast()
  const isEdit = deviceId != null
  const [form, setForm] = useState<FormState>({ groupId: currentGroup, name: '', description: '', deviceType: null, networkBridgeId: null, ipAddress: '' })
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(!isEdit)
  const [confirmRegen, setConfirmRegen] = useState(false)

  const detail = useQuery({
    queryKey: ['device', deviceId],
    queryFn: () => api.get<DeviceDetail>(`/devices/${deviceId}`).then((r) => r.data),
    enabled: isEdit,
  })

  useEffect(() => {
    if (!isEdit || loaded || !detail.data) return
    const d = detail.data
    setForm({ groupId: d.groupId, name: d.name, description: d.description ?? '', deviceType: d.deviceType, networkBridgeId: d.networkBridgeId ?? null, ipAddress: d.ipAddress ?? '' })
    setLoaded(true)
  }, [isEdit, loaded, detail.data])

  const showBridge = form.deviceType != null && BRIDGEABLE.includes(form.deviceType)
  const bridges = useQuery({
    queryKey: ['devices', form.groupId, DeviceTypes.NetworkBridge],
    queryFn: () => api.get<DeviceRow[]>('/devices', { params: { groupId: form.groupId, type: DeviceTypes.NetworkBridge } }).then((r) => r.data),
    enabled: showBridge && form.groupId > 0,
  })

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => {
    setForm((f) => ({ ...f, [k]: v }))
    setErrors((e) => ({ ...e, [k]: undefined }))
  }

  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, networkBridgeId: showBridge ? form.networkBridgeId : null }
      return (isEdit ? api.put<SaveResponse>(`/devices/${deviceId}`, body) : api.post<SaveResponse>('/devices', body)).then((r) => r.data)
    },
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['devices'] })
      qc.invalidateQueries({ queryKey: ['device', res.id] })
      qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
      onSaved(res, !isEdit)
    },
    onError: (e) => setServerError(errorMessage(e)),
  })

  const regenerate = useMutation({
    mutationFn: () => api.post<{ message: string; apiKey: string }>(`/devices/${deviceId}/regenerate-key`).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      setConfirmRegen(false)
      qc.invalidateQueries({ queryKey: ['device', deviceId] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const submit = () => {
    const e: typeof errors = {}
    if (!form.groupId) e.groupId = 'Group is required.'
    if (!form.name.trim()) e.name = 'Device Name is required.'
    if (form.deviceType == null) e.deviceType = 'Device Type is required.'
    if (form.ipAddress.trim() && !IP_RE.test(form.ipAddress.trim())) e.ipAddress = 'Device IP Address must start with http:// or https://'
    setErrors(e)
    setServerError(null)
    if (Object.keys(e).length === 0) save.mutate()
  }

  const bridgeOptions: Option<number>[] = (bridges.data ?? []).filter((b) => b.id !== deviceId).map((b) => ({ value: b.id, label: b.name, sub: b.ipAddress ?? undefined }))

  return (
    <Modal
      open
      onClose={onClose}
      title={isEdit ? 'Edit Device' : 'Add New Device'}
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={submit} disabled={save.isPending || !loaded}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      {!loaded ? (
        detail.isError ? <ErrorBanner message={errorMessage(detail.error)} /> : <LoadingBlock />
      ) : (
        <div className="space-y-4">
          <ErrorBanner message={serverError} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Group" required error={errors.groupId}>
              <SearchSelect
                clearable={false}
                options={groups.map((g) => ({ value: g.id, label: g.name }))}
                value={form.groupId}
                onChange={(v) => {
                  set('groupId', v ?? 0)
                  set('networkBridgeId', null)
                }}
                invalid={!!errors.groupId}
              />
            </Field>
            <Field label="Device Name" required error={errors.name}>
              <input className={errors.name ? 'input input-invalid' : 'input'} value={form.name} maxLength={200} onChange={(e) => set('name', e.target.value)} autoFocus={!isEdit} />
            </Field>
            <Field label="Description" className="sm:col-span-2">
              <textarea className="input" rows={2} value={form.description} maxLength={400} onChange={(e) => set('description', e.target.value)} />
            </Field>
            <Field label="Device Type" required error={errors.deviceType}>
              <select className={errors.deviceType ? 'input input-invalid' : 'input'} value={form.deviceType ?? ''} onChange={(e) => set('deviceType', e.target.value ? Number(e.target.value) : null)}>
                <option value="">Select device type…</option>
                {(lookups.data?.deviceTypes ?? []).map((t) => (
                  <option key={t.value} value={t.value}>
                    {t.label}
                  </option>
                ))}
              </select>
            </Field>
            {showBridge && (
              <Field label="Network Bridge" hint={bridges.data && bridgeOptions.length === 0 ? 'No Network Bridge devices in this group yet.' : 'Optional — the bridge this device connects through.'}>
                <SearchSelect options={bridgeOptions} value={form.networkBridgeId} onChange={(v) => set('networkBridgeId', v)} placeholder={bridges.isLoading ? 'Loading…' : 'Select network bridge…'} />
              </Field>
            )}
            <Field label="Device IP Address" error={errors.ipAddress} hint="[e.g http://192.168.1.1 or https://192.168.1.1]" className={showBridge ? 'sm:col-span-2' : undefined}>
              <input className={errors.ipAddress ? 'input input-invalid' : 'input'} value={form.ipAddress} maxLength={200} placeholder="http://192.168.1.1" onChange={(e) => set('ipAddress', e.target.value)} />
            </Field>
          </div>

          {isEdit && detail.data && (
            <>
              {detail.data.deviceType === DeviceTypes.DispenseMachine && (
                <button type="button" className="btn-secondary" onClick={() => onOpenCanisters(detail.data.id)}>
                  Canister Tint Assignment…
                </button>
              )}
              <ApiKeyPanel apiKey={detail.data.apiKey} onRegenerate={() => setConfirmRegen(true)} />
            </>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirmRegen}
        title="Regenerate API Key"
        confirmLabel="Regenerate"
        busy={regenerate.isPending}
        onClose={() => setConfirmRegen(false)}
        onConfirm={() => regenerate.mutate()}
        message="The current key stops working immediately. The device (or its network bridge) must be updated with the new key. Continue?"
      />
    </Modal>
  )
}

function ApiKeyPanel({ apiKey, onRegenerate }: { apiKey: string; onRegenerate: () => void }) {
  const toast = useToast()
  const endpoint = `${window.location.origin}${import.meta.env.BASE_URL.replace(/\/$/, '')}/api/devices/ingest`
  const snippet = [
    `curl -X POST "${endpoint}" \\`,
    `  -H "X-Api-Key: ${apiKey}" \\`,
    `  -H "Content-Type: application/json" \\`,
    `  -d '{"readings":[{"name":"Temperature","unit":"ºF","value":72.4},{"name":"Humidity","unit":"% RH","value":48}]}'`,
  ].join('\n')

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text)
      toast.success('Copied to clipboard.')
    } catch {
      toast.info('Copy is not available here — select the text and copy it manually.')
    }
  }

  return (
    <div className="rounded-lg border bg-muted/30 p-3 space-y-3">
      <div>
        <div className="label flex items-center gap-1.5">
          <KeyRound className="h-3.5 w-3.5" /> Device API Key
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <code className="flex-1 min-w-0 break-all rounded-md border bg-card px-2.5 py-1.5 font-mono text-xs select-all">{apiKey}</code>
          <button type="button" className="btn-secondary btn-sm" onClick={() => copy(apiKey)}>
            <Copy className="h-3.5 w-3.5" /> Copy
          </button>
          <button type="button" className="btn-secondary btn-sm" onClick={onRegenerate}>
            <RefreshCw className="h-3.5 w-3.5" /> Regenerate
          </button>
        </div>
      </div>
      <div>
        <div className="flex items-center justify-between">
          <div className="label mb-0">How to send readings</div>
          <button type="button" className="btn-ghost btn-sm" onClick={() => copy(snippet)}>
            <Copy className="h-3.5 w-3.5" /> Copy example
          </button>
        </div>
        <p className="text-xs text-muted-foreground mt-1 mb-2">
          POST a batch of readings with the key in the <code className="font-mono">X-Api-Key</code> header. Each reading needs a <code className="font-mono">name</code> and a{' '}
          <code className="font-mono">value</code> (or <code className="font-mono">textValue</code>); <code className="font-mono">unit</code> and an ISO{' '}
          <code className="font-mono">timestamp</code> (UTC) are optional. An empty <code className="font-mono">readings</code> array is a heartbeat.
        </p>
        <pre className="overflow-x-auto rounded-md bg-slate-900 text-slate-100 p-3 text-[11px] leading-relaxed">{snippet}</pre>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Canister Tint Assignment (Dispense Machine)
// ---------------------------------------------------------------------------

const TINT_TYPES: number[] = [MaterialType.Pigment, MaterialType.Dye, MaterialType.Base]
const CANISTERS = Array.from({ length: 16 }, (_, i) => i + 1)

export function CanisterModal({ deviceId, onClose }: { deviceId: number; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [assign, setAssign] = useState<Record<number, number | null> | null>(null)
  const [error, setError] = useState<string | null>(null)

  const detail = useQuery({
    queryKey: ['device', deviceId],
    queryFn: () => api.get<DeviceDetail>(`/devices/${deviceId}`).then((r) => r.data),
  })
  const groupId = detail.data?.groupId
  const materials = useQuery({
    queryKey: ['device-tint-materials', groupId],
    queryFn: () => api.get<MaterialRef[]>('/materials', { params: { groupId } }).then((r) => r.data),
    enabled: !!groupId,
  })

  useEffect(() => {
    if (assign || !detail.data) return
    setAssign(Object.fromEntries(CANISTERS.map((n) => [n, detail.data.canisters.find((c) => c.canisterNo === n)?.materialId ?? null])))
  }, [assign, detail.data])

  const options = useMemo<Option<number>[]>(() => {
    const order = (t: number) => TINT_TYPES.indexOf(t)
    return (materials.data ?? [])
      .filter((m) => TINT_TYPES.includes(m.materialType))
      .sort((a, b) => order(a.materialType) - order(b.materialType) || a.productName.localeCompare(b.productName))
      .map((m) => ({ value: m.id, label: m.productName, sub: [materialTypeLabel[m.materialType], m.productCode].filter(Boolean).join(' · ') }))
  }, [materials.data])

  const save = useMutation({
    mutationFn: () => api.put<{ message: string }>(`/devices/${deviceId}/canisters`, CANISTERS.map((n) => ({ canisterNo: n, materialId: assign?.[n] ?? null }))).then((r) => r.data),
    onSuccess: (res) => {
      toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['device', deviceId] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const assigned = assign ? Object.values(assign).filter(Boolean).length : 0
  const column = (nums: number[]) => (
    <div className="space-y-3">
      {nums.map((n) => (
        <div key={n} className="grid grid-cols-[92px_1fr] items-center gap-2">
          <label className="text-xs font-bold tracking-wide text-muted-foreground">CANISTER {n}</label>
          <SearchSelect options={options} value={assign?.[n] ?? null} onChange={(v) => setAssign((a) => ({ ...(a ?? {}), [n]: v }))} placeholder="Select tint…" emptyText="No Pigment, Dye or Base materials" />
        </div>
      ))}
    </div>
  )

  return (
    <Modal
      open
      onClose={onClose}
      title={
        <span>
          Canister Tint Assignment
          {detail.data && <span className="ml-2 text-sm font-normal text-muted-foreground">{detail.data.name}</span>}
        </span>
      }
      size="xl"
      footer={
        <>
          <span className="mr-auto text-xs text-muted-foreground">{assigned} of 16 canisters assigned</span>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={() => save.mutate()} disabled={save.isPending || !assign}>
            {save.isPending && <Spinner />} Save
          </button>
        </>
      }
    >
      {detail.isLoading || materials.isLoading || !assign ? (
        detail.isError ? <ErrorBanner message={errorMessage(detail.error)} /> : <LoadingBlock />
      ) : (
        <>
          <ErrorBanner message={error} />
          {materials.isError && <ErrorBanner message={errorMessage(materials.error)} />}
          {options.length === 0 && (
            <div className="mb-3 text-sm text-muted-foreground">This group has no Pigment, Dye or Base materials yet. Add them in Equipment &amp; Materials.</div>
          )}
          <div className="grid gap-x-8 gap-y-3 md:grid-cols-2">
            {column(CANISTERS.slice(0, 8))}
            {column(CANISTERS.slice(8))}
          </div>
        </>
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Graphs (telemetry for a day) — one small chart per metric, never a dual axis
// ---------------------------------------------------------------------------

export function GraphsModal({ device, onClose }: { device: DeviceRow; onClose: () => void }) {
  const [date, setDate] = useState(() => format(new Date(), 'yyyy-MM-dd'))
  const q = useQuery({
    queryKey: ['device-metrics', device.id, date],
    queryFn: () =>
      api
        .get<{ date: string; from: string; to: string; series: MetricSeries[] }>(`/devices/${device.id}/metrics`, {
          params: { date, tzOffset: new Date().getTimezoneOffset() },
        })
        .then((r) => r.data),
    refetchInterval: date === format(new Date(), 'yyyy-MM-dd') ? 60_000 : false,
  })
  const shift = (days: number) => setDate((d) => format(addDays(parseISO(d), days), 'yyyy-MM-dd'))
  const domain: [number, number] | undefined = q.data ? [parseUtc(q.data.from).getTime(), parseUtc(q.data.to).getTime()] : undefined

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title={
        <span>
          Graphs <span className="ml-1 text-sm font-normal text-muted-foreground">{device.name} · {device.deviceTypeLabel}</span>
        </span>
      }
      footer={<button className="btn-secondary" onClick={onClose}>Close</button>}
    >
      <div className="flex flex-wrap items-center gap-2 mb-4">
        <button className="btn-icon" onClick={() => shift(-1)} aria-label="Previous day">
          <ChevronLeft className="h-4 w-4" />
        </button>
        <input type="date" className="input w-44" value={date} max={format(new Date(), 'yyyy-MM-dd')} onChange={(e) => e.target.value && setDate(e.target.value)} aria-label="Date" />
        <button className="btn-icon" onClick={() => shift(1)} aria-label="Next day" disabled={date >= format(new Date(), 'yyyy-MM-dd')}>
          <ChevronRight className="h-4 w-4" />
        </button>
        {q.isFetching && <Spinner className="text-muted-foreground" />}
        <span className="ml-auto text-xs text-muted-foreground">{device.lastSeenAt ? `Last seen ${dateTimeSeconds(device.lastSeenAt)}` : 'Never reported'}</span>
      </div>

      {q.isLoading ? (
        <LoadingBlock />
      ) : q.isError ? (
        <ErrorBanner message={errorMessage(q.error)} />
      ) : !q.data?.series.length ? (
        <EmptyState title="No telemetry recorded for this date" icon={<LineChartIcon className="h-5 w-5" />} description="Devices send readings to the ingest endpoint with their API key (see Edit › How to send readings)." />
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {q.data.series.map((s) => (
            <MetricChart key={s.name} series={s} domain={domain} />
          ))}
        </div>
      )}
    </Modal>
  )
}

function MetricChart({ series, domain }: { series: MetricSeries; domain?: [number, number] }) {
  const t = chartTokens(useIsDark())
  const unit = series.unit ? ` ${series.unit}` : ''
  const points = series.points.filter((p) => p.value != null).map((p) => ({ t: parseUtc(p.timestamp).getTime(), value: p.value as number }))
  const texts = series.points.filter((p) => p.value == null && p.textValue)

  return (
    <div className="rounded-lg border p-3 min-w-0">
      <div className="flex flex-wrap items-baseline justify-between gap-2 mb-2">
        <div className="font-semibold">
          {series.name}
          {series.unit && <span className="ml-1 text-xs font-normal text-muted-foreground">({series.unit})</span>}
        </div>
        <div className="text-xs text-muted-foreground tabular-nums">
          {series.latest != null && (
            <>
              latest <span className="font-semibold text-foreground">{num(series.latest, 3)}{unit}</span>
            </>
          )}
          {series.min != null && series.max != null && <> · min {num(series.min, 3)} · max {num(series.max, 3)}</>} · {series.count} reading{series.count === 1 ? '' : 's'}
        </div>
      </div>
      {points.length > 0 ? (
        <div className="h-48" role="img" aria-label={`${series.name} over the day`}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={points} margin={{ top: 6, right: 8, bottom: 0, left: -8 }}>
              <CartesianGrid vertical={false} stroke={t.grid} />
              <XAxis dataKey="t" type="number" scale="time" domain={domain ?? ['dataMin', 'dataMax']} tickFormatter={(v: number) => format(new Date(v), 'HH:mm')} tick={{ fontSize: 11, fill: t.axis }} tickLine={false} axisLine={{ stroke: t.baseline }} minTickGap={24} />
              <YAxis domain={['auto', 'auto']} tick={{ fontSize: 11, fill: t.axis }} tickLine={false} axisLine={false} width={48} tickFormatter={(v: number) => num(v, 2)} />
              <Tooltip
                contentStyle={{ background: t.surface, border: `1px solid ${t.grid}`, borderRadius: 8, fontSize: 12, color: t.ink }}
                labelFormatter={(v) => format(new Date(Number(v)), 'M/d/yyyy h:mm:ss a')}
                formatter={(v) => [`${num(Number(v), 3)}${unit}`, series.name]}
              />
              <Line type="monotone" dataKey="value" stroke={t.series1} strokeWidth={2} dot={points.length <= 40 ? { r: 3, strokeWidth: 0, fill: t.series1 } : false} activeDot={{ r: 5, strokeWidth: 2, stroke: t.surface }} isAnimationActive={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <ul className="text-sm space-y-1 max-h-48 overflow-y-auto">
          {texts.slice(-20).reverse().map((p, i) => (
            <li key={i} className="flex justify-between gap-2">
              <span>{p.textValue}</span>
              <span className="text-xs text-muted-foreground">{format(parseUtc(p.timestamp), 'h:mm:ss a')}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
