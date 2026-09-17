import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, databaseStore, tokenStore } from './api'
import type { GroupRef, Lookups, Me } from './types'

interface AuthState {
  me: Me | undefined
  loading: boolean
  login: (username: string, password: string, remember: boolean) => Promise<void>
  logout: () => void
  refresh: () => Promise<unknown>
}

const AuthContext = createContext<AuthState | null>(null)

const ME_KEY = 'fg.me'

/**
 * The signed-in user as the server last described them, kept next to the session token. It is tied to the token
 * that produced it (a different sign-in, user or database means a different token), and it is only ever a head
 * start: /auth/me is always asked again, and its answer replaces this one.
 */
const cachedMe = {
  stamp: () => (tokenStore.get() ?? '').slice(-24),
  load: (): Me | undefined => {
    try {
      const raw = localStorage.getItem(ME_KEY)
      if (!raw) return undefined
      const { stamp, me } = JSON.parse(raw) as { stamp: string; me: Me }
      return stamp && stamp === cachedMe.stamp() ? me : undefined
    } catch {
      return undefined
    }
  },
  save: (me: Me) => {
    try {
      localStorage.setItem(ME_KEY, JSON.stringify({ stamp: cachedMe.stamp(), me }))
    } catch {
      /* private mode / full storage: the app just waits for /auth/me as before */
    }
  },
  clear: () => localStorage.removeItem(ME_KEY),
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient()
  const [hasToken, setHasToken] = useState(() => !!tokenStore.get())

  const meQuery = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      const me = (await api.get<Me>('/auth/me')).data
      cachedMe.save(me)
      return me
    },
    enabled: hasToken,
    // The last answer opens the page while a fresh one is on its way, so a page's own data is requested at the
    // same time as the session check instead of a round trip later. Roles and groups still come from the server.
    initialData: cachedMe.load,
    initialDataUpdatedAt: 0,
    staleTime: 60_000,
    refetchInterval: 120_000, // keeps the unread-message badge fresh
    retry: false,
  })

  const login = useCallback(
    async (username: string, password: string, remember: boolean) => {
      tokenStore.clear() // a leftover session token would otherwise decide which database the sign-in goes to
      // The server picks the database from the address the site was opened on.
      const res = await api.post<{ token: string }>('/auth/login', { username, password, rememberMe: remember })
      tokenStore.set(res.data.token, remember)
      qc.clear()
      const me = await qc.fetchQuery({ queryKey: ['me'], queryFn: () => api.get<Me>('/auth/me').then((r) => r.data) })
      cachedMe.save(me)
      if (me.database.key !== databaseStore.get()) localStorage.removeItem(GROUP_KEY) // group ids differ between databases
      databaseStore.set(me.database.key)
      setHasToken(true)
    },
    [qc],
  )

  const logout = useCallback(() => {
    tokenStore.clear()
    cachedMe.clear()
    localStorage.removeItem(GROUP_KEY)
    setHasToken(false)
    qc.clear()
  }, [qc])

  const value = useMemo<AuthState>(
    () => ({
      me: hasToken ? meQuery.data : undefined,
      loading: hasToken && meQuery.isLoading,
      login,
      logout,
      refresh: () => meQuery.refetch(),
    }),
    [hasToken, meQuery, login, logout],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

/** Convenience: the logged-in user (pages are only rendered when logged in). */
export function useMe(): Me {
  const { me } = useAuth()
  if (!me) throw new Error('Not logged in')
  return me
}

// ---------------------------------------------------------------------------
// Selected group ("tenant") shared by every module page.
// ---------------------------------------------------------------------------

const GROUP_KEY = 'fg.group'

interface GroupState {
  groupId: number
  group: GroupRef | undefined
  groups: GroupRef[]
  setGroupId: (id: number) => void
}

const GroupContext = createContext<GroupState | null>(null)

export function GroupProvider({ children }: { children: ReactNode }) {
  const { me } = useAuth()
  const [stored, setStored] = useState<number>(() => Number(localStorage.getItem(GROUP_KEY) ?? 0))
  const groups = useMemo(() => me?.groups ?? [], [me])

  const groupId = useMemo(() => {
    if (groups.some((g) => g.id === stored)) return stored
    if (me && groups.some((g) => g.id === me.defaultGroupId)) return me.defaultGroupId
    return groups[0]?.id ?? 0
  }, [groups, stored, me])

  useEffect(() => {
    if (groupId) localStorage.setItem(GROUP_KEY, String(groupId))
  }, [groupId])

  const value = useMemo<GroupState>(
    () => ({ groupId, group: groups.find((g) => g.id === groupId), groups, setGroupId: setStored }),
    [groupId, groups],
  )
  return <GroupContext.Provider value={value}>{children}</GroupContext.Provider>
}

export function useGroup() {
  const ctx = useContext(GroupContext)
  if (!ctx) throw new Error('useGroup must be used inside GroupProvider')
  return ctx
}

export function useLookups() {
  return useQuery({
    queryKey: ['lookups'],
    queryFn: () => api.get<Lookups>('/lookups').then((r) => r.data),
    staleTime: 5 * 60_000,
  })
}
