import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FlaskConical, Pencil, Plus, Search, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { canSeeTab } from '@/lib/access'
import { date } from '@/lib/format'
import { Card, ConfirmDialog, EmptyState, PageHeader, StatCard, Tabs } from '@/components/ui'
import { DataTable, type Column } from '@/components/DataTable'
import { useToast } from '@/components/toast'
import { SampleModal } from './SampleModal'
import { MatchTab } from './MatchTab'
import { PreviewTab } from './PreviewTab'
import { methodLabel, sourceLabel, type ColorSampleRow } from './types'

type TabKey = 'samples' | 'match' | 'preview'

/**
 * Colour matching: the library of measured samples, the search for a formula that hits a wanted colour, and a picture
 * of what that formula should look like on the customer's own wood.
 */
export default function ColorMatchingPage() {
  const { groupId } = useGroup()
  const me = useMe()
  const qc = useQueryClient()
  const toast = useToast()
  const [tab, setTab] = useState<TabKey>('samples')
  const [editing, setEditing] = useState<ColorSampleRow | null>(null)
  const [adding, setAdding] = useState(false)
  const [deleting, setDeleting] = useState<ColorSampleRow | null>(null)

  const samples = useQuery({
    queryKey: ['color-samples', groupId],
    queryFn: () => api.get<ColorSampleRow[]>('/color-matching/samples', { params: { groupId } }).then((r) => r.data),
    enabled: groupId > 0,
  })
  const rows = useMemo(() => samples.data ?? [], [samples.data])

  const remove = useMutation({
    mutationFn: async (id: number) => (await api.delete<{ message: string }>(`/color-matching/samples/${id}`)).data,
    onSuccess: (r) => {
      toast.success(r.message)
      qc.invalidateQueries({ queryKey: ['color-samples'] })
      setDeleting(null)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const species = useMemo(() => new Set(rows.map((r) => r.woodSpecies)).size, [rows])
  const formulas = useMemo(() => new Set(rows.map((r) => r.formulaName.toLowerCase())).size, [rows])
  const onDevice = useMemo(() => rows.filter((r) => r.source === 1).length, [rows])

  const columns: Column<ColorSampleRow>[] = [
    {
      key: 'final',
      header: 'Colour',
      sortValue: (r) => r.final.l,
      cell: (r) => (
        <span className="flex items-center gap-2">
          <span className="h-7 w-7 shrink-0 rounded border" style={{ background: r.final.hex }} title={r.final.hex} />
          <span className="tabular-nums text-xs text-muted-foreground">
            {r.final.l.toFixed(1)} / {r.final.a.toFixed(1)} / {r.final.b.toFixed(1)}
          </span>
        </span>
      ),
    },
    { key: 'woodSpecies', header: 'Wood', cell: (r) => <span className="font-medium">{r.woodSpecies}</span> },
    { key: 'formulaName', header: 'Formula', cell: (r) => r.formulaName },
    {
      key: 'concentration',
      header: 'Strength',
      align: 'right',
      cell: (r) => (r.concentration != null ? `${r.concentration}%` : '—'),
    },
    { key: 'method', header: 'Applied', hideBelow: 'lg', cell: (r) => methodLabel(r.method) || '—' },
    { key: 'coats', header: 'Coats', align: 'right', hideBelow: 'xl', cell: (r) => r.coats ?? '—' },
    { key: 'topcoat', header: 'Topcoat', hideBelow: 'xl', cell: (r) => r.topcoat || '—' },
    { key: 'source', header: 'Read from', hideBelow: 'lg', cell: (r) => sourceLabel(r.source) },
    { key: 'measuredAt', header: 'Measured', hideBelow: 'xl', cell: (r) => (r.measuredAt ? date(r.measuredAt) : '—') },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      cell: (r) => (
        <span className="flex justify-end gap-0.5">
          <button className="btn-icon" title="Edit" onClick={() => setEditing(r)}>
            <Pencil className="h-4 w-4" />
          </button>
          <button className="btn-icon hover:text-destructive" title="Delete" onClick={() => setDeleting(r)}>
            <Trash2 className="h-4 w-4" />
          </button>
        </span>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Color Matching"
        breadcrumbs={['Color Matching']}
        subtitle="Measured samples, the formula that gets closest to a colour, and what it should look like on the wood."
        actions={
          tab === 'samples' ? (
            <button className="btn-primary" onClick={() => setAdding(true)}>
              <Plus className="h-4 w-4" /> New Sample
            </button>
          ) : undefined
        }
      />

      <div className="mb-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Samples" value={rows.length} hint="Every prediction rests on these" icon={<FlaskConical className="h-4 w-4" />} />
        <StatCard label="Woods" value={species} hint="Species recorded" />
        <StatCard label="Formulas" value={formulas} hint="With at least one sample" />
        <StatCard
          label="Device readings"
          value={onDevice}
          tone={onDevice > 0 ? 'success' : 'default'}
          hint={onDevice === rows.length ? 'All read on a spectrophotometer' : `${rows.length - onDevice} from photos or typed in`}
        />
      </div>

      <Tabs
        className="mb-4"
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'samples', label: 'Sample Library', count: rows.length, hidden: !canSeeTab(me, 'colorMatching', 'samples') },
          { key: 'match', label: 'Match a Colour', hidden: !canSeeTab(me, 'colorMatching', 'match') },
          { key: 'preview', label: 'Stain Preview', hidden: !canSeeTab(me, 'colorMatching', 'preview') },
        ]}
      />

      {tab === 'samples' && (
        <DataTable
          rows={rows}
          columns={columns}
          rowKey={(r) => r.id}
          loading={samples.isLoading}
          searchPlaceholder="Search samples…"
          searchText={(r) => [r.name, r.woodSpecies, r.formulaName, r.topcoat, r.notes].filter(Boolean).join(' ')}
          stateKey="color-samples"
          emptyTitle="No samples recorded yet"
          emptyDescription={
            <>
              A sample is one finished piece: the wood, the formula and strength, how it was applied, and the colour it
              measured. Three to five samples of a formula, at different strengths, are enough for it to start
              recommending sensibly.
            </>
          }
          emptyAction={
            <button className="btn-primary" onClick={() => setAdding(true)}>
              <Plus className="h-4 w-4" /> New Sample
            </button>
          }
        />
      )}

      {tab === 'match' && (
        <MatchTab groupId={groupId} samples={rows} onNewSample={() => setAdding(true)} />
      )}

      {tab === 'preview' && <PreviewTab groupId={groupId} samples={rows} />}

      <SampleModal open={adding || !!editing} sample={editing} groupId={groupId} onClose={() => { setAdding(false); setEditing(null) }} />

      <ConfirmDialog
        open={!!deleting}
        message={`Are you sure you want to delete the "${deleting?.name}" sample?`}
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

/** Shown by both the match and preview tabs when there is nothing to work from yet. */
export function NoSamples({ onNewSample }: { onNewSample?: () => void }) {
  return (
    <Card>
      <EmptyState
        icon={<Search className="h-5 w-5" />}
        title="Nothing to match against yet"
        description="Record a few measured samples first — a colour can only be matched to formulas that have been put on wood and measured."
        action={onNewSample ? <button className="btn-primary" onClick={onNewSample}><Plus className="h-4 w-4" /> New Sample</button> : undefined}
      />
    </Card>
  )
}
