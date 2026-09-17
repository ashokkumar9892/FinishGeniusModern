import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Globe, KeyRound, RefreshCw, ShieldAlert, Users } from 'lucide-react'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { ErrorBanner, PageHeader, StatCard } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'

interface LoginEntry {
  id: number
  signedInAtUtc: string
  userName: string
  userId?: number | null
  databaseKey: string
  databaseLabel?: string | null
  succeeded: boolean
  failureReason?: string | null
  isOwner: boolean
  ipAddress?: string | null
  location?: string | null
  city?: string | null
  region?: string | null
  country?: string | null
  timeZone?: string | null
  isp?: string | null
  userAgent?: string | null
  host?: string | null
}

interface LoginActivity {
  items: LoginEntry[]
  total: number
  storedIn?: { key: string; label: string; table: string } | null
  databases: { key: string; label: string }[]
}

const periods = [
  { key: '1', label: 'Last 24 hours', days: 1 },
  { key: '7', label: 'Last 7 days', days: 7 },
  { key: '30', label: 'Last 30 days', days: 30 },
  { key: '90', label: 'Last 90 days', days: 90 },
  { key: 'all', label: 'All time', days: 0 },
] as const
type PeriodKey = (typeof periods)[number]['key']

const TAKE = 2000

/** "Chrome on Windows" from a user-agent string (good enough to tell devices apart). */
function device(ua?: string | null) {
  if (!ua) return ''
  const browser = /Edg\//.test(ua) ? 'Edge'
    : /OPR\/|Opera/.test(ua) ? 'Opera'
    : /Firefox\//.test(ua) ? 'Firefox'
    : /Chrome\//.test(ua) ? 'Chrome'
    : /Safari\//.test(ua) ? 'Safari'
    : ''
  const os = /Windows/.test(ua) ? 'Windows'
    : /iPhone|iPad|iPod/.test(ua) ? 'iOS'
    : /Android/.test(ua) ? 'Android'
    : /Mac OS X|Macintosh/.test(ua) ? 'macOS'
    : /Linux/.test(ua) ? 'Linux'
    : ''
  if (browser && os) return `${browser} on ${os}`
  return browser || os || ua.slice(0, 40)
}

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' })

function ago(iso: string) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 60) return 'just now'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `${hours} h ago`
  return `${Math.round(hours / 24)} days ago`
}

export default function LoginActivityPage() {
  const [period, setPeriod] = useState<PeriodKey>('30')
  const [result, setResult] = useState<'' | 'success' | 'failed'>('')
  const [database, setDatabase] = useState('')

  const query = useQuery({
    queryKey: ['login-activity', period, result, database],
    queryFn: async () => {
      const days = periods.find((p) => p.key === period)?.days ?? 0
      const from = days ? new Date(Date.now() - days * 86_400_000).toISOString() : undefined
      return (await api.get<LoginActivity>('/login-activity', {
        params: { from, result: result || undefined, database: database || undefined, take: TAKE },
      })).data
    },
    refetchInterval: 60_000,
  })

  const rows = useMemo(() => query.data?.items ?? [], [query.data])
  const stats = useMemo(() => {
    const ok = rows.filter((r) => r.succeeded)
    return {
      successful: ok.length,
      failed: rows.length - ok.length,
      users: new Set(ok.map((r) => r.userName.toLowerCase())).size,
      addresses: new Set(rows.map((r) => r.ipAddress).filter(Boolean)).size,
    }
  }, [rows])

  const columns: Column<LoginEntry>[] = [
    {
      key: 'signedInAtUtc',
      header: 'Time',
      sortValue: (r) => r.signedInAtUtc,
      cell: (r) => (
        <div className="whitespace-nowrap">
          <div>{dateTime.format(new Date(r.signedInAtUtc))}</div>
          <div className="text-xs text-muted-foreground">{ago(r.signedInAtUtc)}</div>
        </div>
      ),
    },
    {
      key: 'userName',
      header: 'User',
      cell: (r) => (
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="font-medium">{r.userName}</span>
          {r.isOwner && <span className="badge bg-primary/10 text-primary ring-1 ring-primary/30">Owner</span>}
        </div>
      ),
    },
    {
      key: 'succeeded',
      header: 'Result',
      sortValue: (r) => (r.succeeded ? 1 : 0),
      cell: (r) =>
        r.succeeded ? (
          <span className="badge bg-success/10 text-success ring-1 ring-success/30">Signed in</span>
        ) : (
          <span className="badge bg-destructive/10 text-destructive ring-1 ring-destructive/30" title={r.failureReason ?? undefined}>
            Failed{r.failureReason ? ` — ${r.failureReason}` : ''}
          </span>
        ),
    },
    {
      key: 'ipAddress',
      header: 'IP Address',
      cell: (r) => <span className="font-mono text-xs">{r.ipAddress ?? '—'}</span>,
    },
    {
      key: 'location',
      header: 'Location',
      cell: (r) => (
        <div>
          <div>{r.location ?? <span className="text-muted-foreground">Unknown</span>}</div>
          {(r.isp || r.timeZone) && (
            <div className="text-xs text-muted-foreground">{[r.isp, r.timeZone].filter(Boolean).join(' · ')}</div>
          )}
        </div>
      ),
    },
    {
      key: 'databaseKey',
      header: 'Database',
      hideBelow: 'md',
      cell: (r) => (
        <span className={clsx('badge ring-1', r.databaseKey === 'Prod'
          ? 'bg-destructive/10 text-destructive ring-destructive/30'
          : 'bg-muted text-muted-foreground ring-border')}>
          {r.databaseLabel ?? r.databaseKey}
        </span>
      ),
    },
    {
      key: 'userAgent',
      header: 'Device',
      hideBelow: 'lg',
      sortValue: (r) => device(r.userAgent),
      cell: (r) => (
        <div title={r.userAgent ?? undefined}>
          <div>{device(r.userAgent) || '—'}</div>
          {r.host && <div className="text-xs text-muted-foreground">{r.host}</div>}
        </div>
      ),
    },
  ]

  const storedIn = query.data?.storedIn
  return (
    <>
      <PageHeader
        title="Login Activity"
        breadcrumbs={['Administration', 'Login Activity']}
        subtitle={
          storedIn
            ? `Every sign-in attempt, on every database. Stored in ${storedIn.table} on the ${storedIn.label} database.`
            : 'Every sign-in attempt, on every database.'
        }
        actions={
          <button className="btn-secondary" onClick={() => query.refetch()} disabled={query.isFetching}>
            <RefreshCw className={clsx('h-4 w-4', query.isFetching && 'animate-spin')} /> Refresh
          </button>
        }
      />
      {query.isError && <ErrorBanner message={errorMessage(query.error)} />}

      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard label="Successful sign-ins" value={stats.successful} tone="success" icon={<KeyRound className="h-4 w-4" />} />
        <StatCard label="Failed attempts" value={stats.failed} tone={stats.failed ? 'danger' : 'default'} icon={<ShieldAlert className="h-4 w-4" />} />
        <StatCard label="Users signed in" value={stats.users} icon={<Users className="h-4 w-4" />} />
        <StatCard label="IP addresses" value={stats.addresses} icon={<Globe className="h-4 w-4" />} />
      </div>

      {query.data && query.data.total > rows.length && (
        <div className="mb-3 text-xs text-muted-foreground">
          Showing the newest {rows.length.toLocaleString()} of {query.data.total.toLocaleString()} attempts — pick a shorter period to see the rest.
        </div>
      )}

      <DataTable
        rows={rows}
        columns={columns}
        rowKey={(r) => r.id}
        loading={query.isLoading}
        searchPlaceholder="Search user, IP, location…"
        searchText={(r) => [r.userName, r.ipAddress, r.location, r.isp, r.failureReason, device(r.userAgent), r.databaseLabel].filter(Boolean).join(' ')}
        initialSort={{ key: 'signedInAtUtc', dir: 'desc' }}
        rowClassName={(r) => (r.succeeded ? undefined : 'bg-destructive/5')}
        toolbar={
          <div className="flex flex-wrap gap-2">
            <select className="input w-auto" value={period} onChange={(e) => setPeriod(e.target.value as PeriodKey)} aria-label="Period">
              {periods.map((p) => <option key={p.key} value={p.key}>{p.label}</option>)}
            </select>
            <select className="input w-auto" value={result} onChange={(e) => setResult(e.target.value as typeof result)} aria-label="Result">
              <option value="">All results</option>
              <option value="success">Signed in</option>
              <option value="failed">Failed</option>
            </select>
            <select className="input w-auto" value={database} onChange={(e) => setDatabase(e.target.value)} aria-label="Database">
              <option value="">All databases</option>
              {query.data?.databases.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}
            </select>
          </div>
        }
        emptyTitle="No sign-ins in this period"
        emptyDescription="Sign-in attempts appear here as soon as someone signs in."
      />
    </>
  )
}
