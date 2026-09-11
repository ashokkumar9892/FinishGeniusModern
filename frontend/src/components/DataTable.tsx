import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight } from 'lucide-react'
import clsx from 'clsx'
import { EmptyState, LoadingBlock, SearchInput } from './ui'

export interface Column<T> {
  key: string
  header: ReactNode
  cell?: (row: T) => ReactNode
  /** Value used for sorting and (by default) searching. Defaults to row[key]. */
  sortValue?: (row: T) => string | number | null | undefined
  sortable?: boolean
  align?: 'left' | 'right' | 'center'
  className?: string
  /** Hide the column below a breakpoint to keep tables readable on small screens. */
  hideBelow?: 'sm' | 'md' | 'lg' | 'xl'
}

export interface DataTableProps<T> {
  rows: T[]
  columns: Column<T>[]
  rowKey: (row: T) => string | number
  loading?: boolean
  /** Built-in search box; omit to hide it. */
  searchPlaceholder?: string
  /** Text used for search per row; defaults to all column values joined. */
  searchText?: (row: T) => string
  /** External search text (when the page owns the search box). */
  search?: string
  initialSort?: { key: string; dir: 'asc' | 'desc' }
  pageSize?: number
  selectable?: boolean
  selected?: (string | number)[]
  onSelectedChange?: (keys: (string | number)[]) => void
  onRowClick?: (row: T) => void
  rowClassName?: (row: T) => string | undefined
  emptyTitle?: string
  emptyDescription?: ReactNode
  emptyAction?: ReactNode
  /** Extra controls rendered left of the search box. */
  toolbar?: ReactNode
  /** Rendered right of the search box. */
  toolbarRight?: ReactNode
  dense?: boolean
  /** Remove the card border (when embedding in another card/modal). */
  bare?: boolean
  /** Remembers the current page in sessionStorage under this key (e.g. to come back to the same page from a detail screen). */
  stateKey?: string
}

const hide = { sm: 'hidden sm:table-cell', md: 'hidden md:table-cell', lg: 'hidden lg:table-cell', xl: 'hidden xl:table-cell' }

function defaultValue<T>(row: T, col: Column<T>) {
  if (col.sortValue) return col.sortValue(row)
  const v = (row as Record<string, unknown>)[col.key]
  return typeof v === 'number' || typeof v === 'string' ? v : v == null ? '' : String(v)
}

/**
 * Client-side grid in the spirit of the legacy DataTables screens: sortable headers, search,
 * 50 rows per page, multi-row selection and "Showing X to Y of Z entries (filtered from N total entries)".
 */
export function DataTable<T>(props: DataTableProps<T>) {
  const {
    rows, columns, rowKey, loading, searchPlaceholder, searchText, initialSort, pageSize = 50, selectable,
    selected = [], onSelectedChange, onRowClick, rowClassName, emptyTitle = 'No data available in table',
    emptyDescription, emptyAction, toolbar, toolbarRight, dense, bare, stateKey,
  } = props
  const [q, setQ] = useState('')
  const [sort, setSort] = useState(initialSort)
  const pageStoreKey = stateKey ? `fg.table.${stateKey}.page` : null
  const [page, setPage] = useState(() => {
    if (!pageStoreKey) return 0
    try {
      return Math.max(0, Number(sessionStorage.getItem(pageStoreKey) ?? 0) || 0)
    } catch {
      return 0
    }
  })
  // With a stateKey the first load of the rows keeps the remembered page; later search / row changes go back to page 1.
  const rowsLoaded = useRef(!pageStoreKey)
  useEffect(() => {
    if (!pageStoreKey) return
    try {
      sessionStorage.setItem(pageStoreKey, String(page))
    } catch {
      /* storage unavailable */
    }
  }, [pageStoreKey, page])
  const search = (props.search ?? q).trim().toLowerCase()

  const filtered = useMemo(() => {
    if (!search) return rows
    return rows.filter((r) => {
      const text = searchText ? searchText(r) : columns.map((c) => defaultValue(r, c) ?? '').join(' ')
      return text.toLowerCase().includes(search)
    })
  }, [rows, search, searchText, columns])

  const sorted = useMemo(() => {
    if (!sort) return filtered
    const col = columns.find((c) => c.key === sort.key)
    if (!col) return filtered
    const dir = sort.dir === 'asc' ? 1 : -1
    return [...filtered].sort((a, b) => {
      const va = defaultValue(a, col)
      const vb = defaultValue(b, col)
      if (va == null || va === '') return 1
      if (vb == null || vb === '') return -1
      if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir
      return String(va).localeCompare(String(vb), undefined, { numeric: true, sensitivity: 'base' }) * dir
    })
  }, [filtered, sort, columns])

  const pages = Math.max(1, Math.ceil(sorted.length / pageSize))
  useEffect(() => {
    if (!rowsLoaded.current) {
      if (rows.length > 0) rowsLoaded.current = true
      return
    }
    setPage(0)
  }, [search, rows.length])
  const current = Math.min(page, pages - 1)
  const pageRows = sorted.slice(current * pageSize, current * pageSize + pageSize)
  const from = sorted.length === 0 ? 0 : current * pageSize + 1
  const to = Math.min(sorted.length, (current + 1) * pageSize)

  const selectedSet = new Set(selected)
  const toggle = (key: string | number) => {
    if (!onSelectedChange) return
    const next = new Set(selectedSet)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    onSelectedChange([...next])
  }
  const allOnPage = pageRows.length > 0 && pageRows.every((r) => selectedSet.has(rowKey(r)))
  const toggleAll = () => {
    if (!onSelectedChange) return
    const next = new Set(selectedSet)
    pageRows.forEach((r) => (allOnPage ? next.delete(rowKey(r)) : next.add(rowKey(r))))
    onSelectedChange([...next])
  }

  const onHeader = (col: Column<T>) => {
    if (col.sortable === false) return
    setSort((s) => (s?.key === col.key ? { key: col.key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key: col.key, dir: 'asc' }))
  }

  return (
    <div className={clsx(!bare && 'card overflow-hidden')}>
      {(searchPlaceholder !== undefined || toolbar || toolbarRight) && (
        <div className={clsx('flex flex-wrap items-center gap-2 no-print', bare ? 'pb-3' : 'p-3 border-b')}>
          {toolbar}
          <div className="flex-1" />
          {searchPlaceholder !== undefined && props.search === undefined && (
            <SearchInput value={q} onChange={setQ} placeholder={searchPlaceholder} className="w-full sm:w-72" />
          )}
          {toolbarRight}
        </div>
      )}
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-muted/60 border-b">
            <tr>
              {selectable && (
                <th className="th w-8">
                  <input type="checkbox" className="h-4 w-4 accent-[hsl(var(--primary))]" checked={allOnPage} onChange={toggleAll} aria-label="Select all" />
                </th>
              )}
              {columns.map((c) => {
                const active = sort?.key === c.key
                return (
                  <th
                    key={c.key}
                    className={clsx('th', c.hideBelow && hide[c.hideBelow], c.sortable !== false && 'cursor-pointer select-none hover:text-foreground', c.align === 'right' && 'text-right', c.align === 'center' && 'text-center', c.className)}
                    onClick={() => onHeader(c)}
                  >
                    <span className="inline-flex items-center gap-1">
                      {c.header}
                      {c.sortable !== false &&
                        (active ? (sort!.dir === 'asc' ? <ArrowUp className="h-3 w-3 text-primary" /> : <ArrowDown className="h-3 w-3 text-primary" />) : <ArrowUpDown className="h-3 w-3 opacity-40" />)}
                    </span>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={columns.length + (selectable ? 1 : 0)}>
                  <LoadingBlock />
                </td>
              </tr>
            ) : pageRows.length === 0 ? (
              <tr>
                <td colSpan={columns.length + (selectable ? 1 : 0)}>
                  <EmptyState title={search ? 'No matching records found' : emptyTitle} description={search ? undefined : emptyDescription} action={search ? undefined : emptyAction} />
                </td>
              </tr>
            ) : (
              pageRows.map((r) => {
                const key = rowKey(r)
                const isSel = selectedSet.has(key)
                return (
                  <tr
                    key={key}
                    className={clsx('border-b last:border-0 transition-colors', isSel ? 'bg-accent' : 'hover:bg-muted/40', (onRowClick || selectable) && 'cursor-pointer', rowClassName?.(r))}
                    onClick={(e) => {
                      if ((e.target as HTMLElement).closest('button,a,input,select,textarea,label')) return
                      if (onRowClick) onRowClick(r)
                      else if (selectable) toggle(key)
                    }}
                  >
                    {selectable && (
                      <td className="td w-8">
                        <input type="checkbox" className="h-4 w-4 accent-[hsl(var(--primary))]" checked={isSel} onChange={() => toggle(key)} aria-label="Select row" />
                      </td>
                    )}
                    {columns.map((c) => {
                      const text = c.cell ? null : String(defaultValue(r, c) ?? '')
                      return (
                        <td
                          key={c.key}
                          className={clsx('td', dense && 'py-1.5', c.hideBelow && hide[c.hideBelow], c.align === 'right' && 'text-right', c.align === 'center' && 'text-center',
                            // short plain values (ids, group names, codes) stay on one line
                            text !== null && text.length <= 40 && 'whitespace-nowrap', c.className)}
                        >
                          {c.cell ? c.cell(r) : text}
                        </td>
                      )
                    })}
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
      </div>
      {!loading && (
        <div className={clsx('flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground no-print', bare ? 'pt-3' : 'px-3 py-2 border-t')}>
          <div>
            Showing {from} to {to} of {sorted.length.toLocaleString()} entries
            {search && rows.length !== filtered.length && ` (filtered from ${rows.length.toLocaleString()} total entries)`}
            {selectable && selected.length > 0 && <span className="ml-2 font-medium text-foreground">{selected.length} rows selected</span>}
          </div>
          {pages > 1 && (
            <div className="flex items-center gap-1">
              <button className="btn-ghost btn-sm" disabled={current === 0} onClick={() => setPage(current - 1)}>
                <ChevronLeft className="h-3.5 w-3.5" /> Previous
              </button>
              {Array.from({ length: pages }, (_, i) => i)
                .filter((i) => i === 0 || i === pages - 1 || Math.abs(i - current) <= 2)
                .map((i, idx, arr) => (
                  <span key={i} className="flex items-center">
                    {idx > 0 && arr[idx - 1] !== i - 1 && <span className="px-1">…</span>}
                    <button className={clsx('btn-sm btn min-w-8', i === current ? 'bg-primary text-primary-foreground' : 'hover:bg-muted')} onClick={() => setPage(i)}>
                      {i + 1}
                    </button>
                  </span>
                ))}
              <button className="btn-ghost btn-sm" disabled={current >= pages - 1} onClick={() => setPage(current + 1)}>
                Next <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
