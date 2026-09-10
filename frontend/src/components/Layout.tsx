import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import {
  BookOpen, Building2, Calculator, CalendarRange, ChevronsLeft, ChevronsRight, ClipboardCheck, DollarSign, FlaskConical,
  HelpCircle, Images, KeyRound, Layers, LayoutDashboard, ListOrdered, LogOut, Mail, Menu, Moon, Package, Sun, Tags, Upload, UserCog, Users,
} from 'lucide-react'
import clsx from 'clsx'
import { useAuth, useGroup } from '@/lib/auth'
import { canAccess, isSystemAdmin, type ModuleKey } from '@/lib/access'
import { SearchSelect } from './SearchSelect'
import { AgreementModal } from './AgreementModal'

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  module: ModuleKey
}

export const navSections: { title: string; items: NavItem[] }[] = [
  {
    title: 'Operations',
    items: [
      { to: '/dashboard', label: 'Dashboard', icon: <LayoutDashboard />, module: 'dashboard' },
      { to: '/my-work', label: 'My Work', icon: <ClipboardCheck />, module: 'myWork' },
      { to: '/work-instructions', label: 'Work Instructions', icon: <BookOpen />, module: 'workInstructions' },
    ],
  },
  {
    title: 'Processes',
    items: [
      { to: '/process-steps', label: 'Process Steps', icon: <ListOrdered />, module: 'processSteps' },
      { to: '/process-schedules', label: 'Process Schedules', icon: <CalendarRange />, module: 'processSchedules' },
      { to: '/material-quantities', label: 'Material Quantities', icon: <Calculator />, module: 'materialQuantities' },
      { to: '/pricing', label: 'Pricing', icon: <DollarSign />, module: 'pricing' },
    ],
  },
  {
    title: 'Formulation',
    items: [
      { to: '/materials', label: 'Equipment & Materials', icon: <Package />, module: 'materials' },
      { to: '/formulas', label: 'Formulas', icon: <FlaskConical />, module: 'formulas' },
      { to: '/photos', label: 'Photo Gallery', icon: <Images />, module: 'photos' },
    ],
  },
  {
    title: 'Administration',
    items: [
      { to: '/groups', label: 'Groups', icon: <Building2 />, module: 'groups' },
      { to: '/users', label: 'Users', icon: <Users />, module: 'users' },
      { to: '/material-categories', label: 'Material Categories', icon: <Tags />, module: 'materialCategories' },
      { to: '/sub-steps', label: 'Sub Step Setup', icon: <Layers />, module: 'subSteps' },
      { to: '/import', label: 'Import', icon: <Upload />, module: 'import' },
    ],
  },
]

const NAV_KEY = 'finish-genius.nav'
const THEME_KEY = 'fg.theme'

/** The official Finish Genius PRO badge (public/brand/fg-logo.png, 149×120). */
export const logoSrc = import.meta.env.BASE_URL + 'brand/fg-logo.png'

export function Logo({ compact, size = 'md' }: { compact?: boolean; size?: 'md' | 'lg' }) {
  return (
    <img
      src={logoSrc}
      alt="Finish Genius PRO"
      className={clsx('shrink-0 select-none drop-shadow', compact ? 'h-9' : size === 'lg' ? 'h-24' : 'h-12')}
      draggable={false}
    />
  )
}

function useClickOutside(onOutside: () => void) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && onOutside()
    document.addEventListener('mousedown', h)
    return () => document.removeEventListener('mousedown', h)
  }, [onOutside])
  return ref
}

function Dropdown({ trigger, children }: { trigger: ReactNode; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false)
  const ref = useClickOutside(() => setOpen(false))
  return (
    <div ref={ref} className="relative">
      <div onClick={() => setOpen((o) => !o)}>{trigger}</div>
      {open && <div className="absolute right-0 mt-2 w-60 card shadow-xl p-1 z-40">{children(() => setOpen(false))}</div>}
    </div>
  )
}

const menuItem = 'flex w-full items-center gap-2 rounded px-2.5 py-2 text-sm hover:bg-muted text-left'

export function Layout() {
  const { me, logout } = useAuth()
  const { groupId, groups, setGroupId } = useGroup()
  const navigate = useNavigate()
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(NAV_KEY) === 'collapsed')
  const [mobileOpen, setMobileOpen] = useState(false)
  const [theme, setTheme] = useState(() => localStorage.getItem(THEME_KEY) ?? 'light')

  useEffect(() => localStorage.setItem(NAV_KEY, collapsed ? 'collapsed' : 'expanded'), [collapsed])
  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(THEME_KEY, theme)
  }, [theme])

  if (!me) return null
  const initials = ((me.firstName?.[0] ?? '') + (me.lastName?.[0] ?? '') || me.username[0]).toUpperCase()

  const sidebar = (
    <nav className="flex-1 overflow-y-auto py-3 space-y-4">
      {navSections.map((s) => {
        const items = s.items.filter((i) => canAccess(me, i.module))
        if (items.length === 0) return null
        return (
          <div key={s.title}>
            {!collapsed && <div className="px-4 pb-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-sidebar-muted">{s.title}</div>}
            <ul className="space-y-0.5 px-2">
              {items.map((i) => (
                <li key={i.to}>
                  <NavLink
                    to={i.to}
                    onClick={() => setMobileOpen(false)}
                    title={collapsed ? i.label : undefined}
                    className={({ isActive }) =>
                      clsx(
                        'flex items-center gap-3 rounded-md px-2.5 py-2 text-sm font-medium transition-colors [&_svg]:h-[18px] [&_svg]:w-[18px] [&_svg]:shrink-0',
                        isActive ? 'bg-sidebar-accent text-white shadow' : 'text-sidebar-foreground/80 hover:bg-white/5 hover:text-white',
                        collapsed && 'justify-center',
                      )
                    }
                  >
                    {i.icon}
                    {!collapsed && <span className="truncate">{i.label}</span>}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        )
      })}
    </nav>
  )

  return (
    <div className="flex h-full">
      {/* Sidebar */}
      <aside
        className={clsx(
          'bg-sidebar text-sidebar-foreground flex flex-col border-r border-sidebar-border transition-all duration-200 no-print',
          'fixed inset-y-0 left-0 z-40 lg:static',
          collapsed ? 'w-[68px]' : 'w-64',
          mobileOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0',
        )}
      >
        <Link to="/" className={clsx('flex items-center h-16 border-b border-sidebar-border', collapsed ? 'justify-center' : 'px-4')}>
          <Logo compact={collapsed} />
        </Link>
        {sidebar}
        <button className="hidden lg:flex items-center justify-center gap-2 h-10 border-t border-sidebar-border text-sidebar-muted hover:text-white text-xs" onClick={() => setCollapsed((c) => !c)}>
          {collapsed ? <ChevronsRight className="h-4 w-4" /> : <><ChevronsLeft className="h-4 w-4" /> Collapse</>}
        </button>
      </aside>
      {mobileOpen && <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMobileOpen(false)} />}

      <div className="flex-1 flex flex-col min-w-0">
        {/* Header */}
        <header className="h-16 shrink-0 flex items-center gap-3 border-b bg-card px-3 sm:px-5 no-print">
          <button className="btn-icon lg:hidden" onClick={() => setMobileOpen(true)} aria-label="Menu">
            <Menu className="h-5 w-5" />
          </button>
          <div className="flex items-center gap-2 min-w-0">
            <span className="hidden md:inline text-xs font-medium text-muted-foreground">Group</span>
            <SearchSelect
              className="w-52 sm:w-72"
              clearable={false}
              options={groups.map((g) => ({ value: g.id, label: g.name }))}
              value={groupId}
              onChange={(v) => v && setGroupId(v)}
              placeholder="Select group"
            />
          </div>
          <div className="flex-1" />

          <Dropdown
            trigger={
              <button className="btn-ghost h-9 px-2.5" title="Help & support">
                <HelpCircle className="h-5 w-5" />
                <span className="hidden xl:inline">Help & support</span>
              </button>
            }
          >
            {(close) => (
              <>
                <button className={menuItem} onClick={() => { close(); navigate('/messages?compose=Support') }}>
                  <HelpCircle className="h-4 w-4" /> FG APP Support
                </button>
                <button className={menuItem} onClick={() => { close(); navigate('/messages?compose=Question') }}>
                  <FlaskConical className="h-4 w-4" /> Finishing Questions
                </button>
                {isSystemAdmin(me) && (
                  <button className={menuItem} onClick={() => { close(); navigate('/messages?compose=Internal') }}>
                    <UserCog className="h-4 w-4" /> Internal DPM
                  </button>
                )}
              </>
            )}
          </Dropdown>

          <Link to="/messages" className="btn-ghost h-9 px-2.5 relative" title="DPM Center (messages)">
            <Mail className="h-5 w-5" />
            <span className="hidden xl:inline">DPM Center</span>
            {me.unreadMessages > 0 && (
              <span className="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] rounded-full bg-primary text-[10px] font-bold text-white grid place-items-center px-1">
                {me.unreadMessages > 99 ? '99+' : me.unreadMessages}
              </span>
            )}
          </Link>

          <button className="btn-icon" onClick={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))} title="Toggle theme">
            {theme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </button>

          <Dropdown
            trigger={
              <button className="flex items-center gap-2 rounded-full hover:bg-muted pl-1 pr-2 py-1">
                <span className="h-8 w-8 rounded-full bg-primary/15 text-primary grid place-items-center text-xs font-bold">{initials}</span>
                <span className="hidden md:block text-left leading-tight">
                  <span className="block text-sm font-medium max-w-[160px] truncate">Hello, {me.firstName || me.username}</span>
                  <span className="block text-xs text-muted-foreground max-w-[160px] truncate">{me.email}</span>
                </span>
              </button>
            }
          >
            {(close) => (
              <>
                <div className="px-2.5 py-2 border-b mb-1">
                  <div className="text-sm font-medium truncate">{me.username}</div>
                  <div className="text-xs text-muted-foreground truncate">{me.email}</div>
                </div>
                <button className={menuItem} onClick={() => { close(); navigate('/profile') }}>
                  <KeyRound className="h-4 w-4" /> Profile & password
                </button>
                <button className={menuItem} onClick={() => { close(); logout(); navigate('/login') }}>
                  <LogOut className="h-4 w-4" /> Sign out
                </button>
              </>
            )}
          </Dropdown>
        </header>

        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-[1600px] p-4 sm:p-6">
            {groupId ? <Outlet /> : <div className="card p-8 text-center text-muted-foreground">You are not assigned to any group yet. Contact your administrator.</div>}
          </div>
          <footer className="px-6 pb-4 text-right text-xs text-muted-foreground no-print">© AWFI {new Date().getFullYear()}</footer>
        </main>
      </div>
      {!me.agreementAccepted && <AgreementModal />}
    </div>
  )
}
