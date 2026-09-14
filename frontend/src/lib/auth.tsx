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

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient()
  const [hasToken, setHasToken] = useState(() => !!tokenStore.get())

  const meQuery = useQuery({
    queryKey: ['me'],
    queryFn: () => api.get<Me>('/auth/me').then((r) => r.data),
    enabled: hasToken,
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
      if (me.database.key !== databaseStore.get()) localStorage.removeItem(GROUP_KEY) // group ids differ between databases
      databaseStore.set(me.database.key)
      setHasToken(true)
    },
    [qc],
  )

  const logout = useCallback(() => {
    tokenStore.clear()
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
