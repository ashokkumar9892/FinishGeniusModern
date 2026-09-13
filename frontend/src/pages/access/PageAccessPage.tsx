import { Fragment, useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronDown, ChevronRight, Eye, EyeOff, RotateCcw, Save } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { Card, ErrorBanner, LoadingBlock, Note, PageHeader, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'

interface AccessRole {
  key: string
  label: string
}
interface AccessTab {
  key: string
  label: string
  allowedRoles: string[]
}
interface AccessPage {
  key: string
  label: string
  defaultRoles: string[]
  allowedRoles: string[]
  tabs: AccessTab[]
}
interface AccessState {
  roles: AccessRole[]
  sections: { title: string; pages: AccessPage[] }[]
  updatedAt?: string | null
  updatedBy?: string | null
}

/** Page key, or "page.tab", → roles it is on for. */
type Grid = Record<string, string[]>

const tabKey = (page: AccessPage, tab: AccessTab) => `${page.key}.${tab.key}`

function toGrid(state: AccessState): Grid {
  const grid: Grid = {}
  for (const section of state.sections)
    for (const page of section.pages) {
      grid[page.key] = [...page.allowedRoles]
      for (const tab of page.tabs) grid[tabKey(page, tab)] = [...tab.allowedRoles]
    }
  return grid
}

const fingerprint = (grid: Grid) => JSON.stringify(Object.keys(grid).sort().map((k) => [k, [...grid[k]].sort()]))

function Cell({ available, checked, disabled, onChange, title }: {
  available: boolean
  checked: boolean
  disabled?: boolean
  onChange: (on: boolean) => void
  title?: string
}) {
  if (!available) return <span className="text-muted-foreground/40" title="This role never has this page">—</span>
  return (
    <input
      type="checkbox"
      title={title}
      className="h-4 w-4 cursor-pointer accent-[hsl(var(--primary))] disabled:cursor-not-allowed disabled:opacity-40"
      checked={checked}
      disabled={disabled}
      onChange={(e) => onChange(e.target.checked)}
    />
  )
}

export default function PageAccessPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const state = useQuery({ queryKey: ['page-access'], queryFn: async () => (await api.get<AccessState>('/access')).data })
  const [grid, setGrid] = useState<Grid>({})
  const [open, setOpen] = useState<Record<string, boolean>>({})
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (state.data) setGrid(toGrid(state.data))
  }, [state.data])

  const pages = useMemo(() => state.data?.sections.flatMap((s) => s.pages) ?? [], [state.data])
  const saved = useMemo(() => (state.data ? toGrid(state.data) : {}), [state.data])
  const dirty = !!state.data && fingerprint(grid) !== fingerprint(saved)

  const has = (key: string, role: string) => grid[key]?.includes(role) ?? false
  const apply = (changes: [key: string, role: string, on: boolean][]) =>
    setGrid((prev) => {
      const next = { ...prev }
      for (const [key, role, on] of changes) {
        const roles = new Set(next[key] ?? [])
        if (on) roles.add(role)
        else roles.delete(role)
        next[key] = [...roles]
      }
      return next
    })

  /** Switching on also switches the page's tabs on; switching off only the page (its tabs come back with it). */
  const switchesFor = (page: AccessPage, role: string, on: boolean): [string, string, boolean][] =>
    !page.defaultRoles.includes(role) ? [] : on ? [[page.key, role, true], ...page.tabs.map((t) => [tabKey(page, t), role, true] as [string, string, boolean])] : [[page.key, role, false]]

  const setRole = (role: string, on: boolean) => apply(pages.flatMap((p) => switchesFor(p, role, on)))
  const setPage = (page: AccessPage, on: boolean) => apply(page.defaultRoles.flatMap((r) => switchesFor(page, r, on)))
  const setEverything = (on: boolean) => apply(pages.flatMap((p) => p.defaultRoles.flatMap((r) => switchesFor(p, r, on))))

  const save = useMutation({
    mutationFn: async () => {
      const body: { pages: Grid; tabs: Grid } = { pages: {}, tabs: {} }
      for (const p of pages) {
        body.pages[p.key] = grid[p.key] ?? []
        for (const t of p.tabs) body.tabs[tabKey(p, t)] = grid[tabKey(p, t)] ?? []
      }
      return (await api.put<{ message: string }>('/access', body)).data
    },
    onSuccess: (r) => {
      toast.success(r.message)
      setError(null)
      qc.invalidateQueries({ queryKey: ['page-access'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  if (state.isLoading) return <LoadingBlock />
  if (!state.data) return <ErrorBanner message={errorMessage(state.error)} />
  const roles = state.data.roles
  const pagesOn = (role: string) => pages.filter((p) => has(p.key, role)).length
  const pagesPossible = (role: string) => pages.filter((p) => p.defaultRoles.includes(role)).length

  return (
    <>
      <PageHeader
        title="Page Access"
        breadcrumbs={['Page Access']}
        subtitle={
          state.data.updatedAt
            ? `Last saved ${new Date(state.data.updatedAt).toLocaleString()} by ${state.data.updatedBy ?? 'owner'}.`
            : 'Every role currently sees all of its pages.'
        }
        actions={
          <>
            <button className="btn-secondary" onClick={() => setEverything(true)}>
              <Eye className="h-4 w-4" /> Show everything
            </button>
            <button className="btn-secondary" onClick={() => setEverything(false)}>
              <EyeOff className="h-4 w-4" /> Hide everything
            </button>
            <button className="btn-secondary" disabled={!dirty || save.isPending} onClick={() => setGrid(saved)}>
              <RotateCcw className="h-4 w-4" /> Discard
            </button>
            <button className="btn-primary" disabled={!dirty || save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? <Spinner /> : <Save className="h-4 w-4" />} Save
            </button>
          </>
        }
      />
      <ErrorBanner message={error} />
      <div className="mb-4">
        <Note tone="info">
          Tick what each role may see; untick to hide a page or tab from every user with that role. Use a role's <b>All</b> /{' '}
          <b>None</b> to change it for everyone in one go, or the eye buttons to change one page for all roles. A role never gets
          pages it doesn't normally have (—). Users with several roles see a page when any of their roles has it. Only this
          owner account can open this page, and it always sees everything.
        </Note>
      </div>

      <Card bodyClassName="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 border-b">
              <tr>
                <th className="px-4 py-3 text-left font-medium min-w-[240px]">Page / tab</th>
                {roles.map((r) => (
                  <th key={r.key} className="px-3 py-3 text-center font-medium min-w-[130px] align-top">
                    <div>{r.label}</div>
                    <div className="text-xs font-normal text-muted-foreground">
                      {pagesOn(r.key)} of {pagesPossible(r.key)} pages
                    </div>
                    <div className="mt-1.5 flex justify-center gap-1">
                      <button className="btn-ghost btn-sm h-6 px-2" onClick={() => setRole(r.key, true)} title={`Show every page to ${r.label}`}>
                        All
                      </button>
                      <button className="btn-ghost btn-sm h-6 px-2" onClick={() => setRole(r.key, false)} title={`Hide every page from ${r.label}`}>
                        None
                      </button>
                    </div>
                  </th>
                ))}
                <th className="px-3 py-3 text-center font-medium align-top">All roles</th>
              </tr>
            </thead>
            <tbody>
              {state.data.sections.map((section) => (
                <Fragment key={section.title}>
                  <tr>
                    <td colSpan={roles.length + 2} className="px-4 pt-4 pb-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                      {section.title}
                    </td>
                  </tr>
                  {section.pages.map((page) => (
                    <Fragment key={page.key}>
                      <tr className="border-t hover:bg-muted/30">
                        <td className="px-4 py-2">
                          {page.tabs.length > 0 ? (
                            <button
                              type="button"
                              className="flex items-center gap-1 font-medium text-left"
                              onClick={() => setOpen((o) => ({ ...o, [page.key]: !o[page.key] }))}
                            >
                              {open[page.key] ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                              {page.label}
                              <span className="text-xs font-normal text-muted-foreground">({page.tabs.length} tabs)</span>
                            </button>
                          ) : (
                            <span className="pl-5 font-medium">{page.label}</span>
                          )}
                        </td>
                        {roles.map((r) => (
                          <td key={r.key} className="px-3 py-2 text-center">
                            <Cell
                              available={page.defaultRoles.includes(r.key)}
                              checked={has(page.key, r.key)}
                              onChange={(on) => apply([[page.key, r.key, on]])}
                              title={`${page.label} for ${r.label}`}
                            />
                          </td>
                        ))}
                        <td className="px-3 py-2">
                          <div className="flex justify-center gap-1">
                            <button className="btn-icon h-7 w-7" title="Show to every role" onClick={() => setPage(page, true)}>
                              <Eye className="h-4 w-4" />
                            </button>
                            <button className="btn-icon h-7 w-7" title="Hide from every role" onClick={() => setPage(page, false)}>
                              <EyeOff className="h-4 w-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                      {open[page.key] &&
                        page.tabs.map((tab) => (
                          <tr key={tab.key} className="bg-muted/20">
                            <td className="py-1.5 pl-12 pr-4 text-muted-foreground">{tab.label}</td>
                            {roles.map((r) => (
                              <td key={r.key} className="px-3 py-1.5 text-center">
                                <Cell
                                  available={page.defaultRoles.includes(r.key)}
                                  disabled={!has(page.key, r.key)}
                                  checked={has(page.key, r.key) && has(tabKey(page, tab), r.key)}
                                  onChange={(on) => apply([[tabKey(page, tab), r.key, on]])}
                                  title={has(page.key, r.key) ? `${page.label} › ${tab.label} for ${r.label}` : 'Turn the page on first'}
                                />
                              </td>
                            ))}
                            <td />
                          </tr>
                        ))}
                    </Fragment>
                  ))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </>
  )
}
