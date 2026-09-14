import axios, { AxiosError } from 'axios'

const TOKEN_KEY = 'fg.token'

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY),
  set: (token: string, remember: boolean) => {
    tokenStore.clear()
    ;(remember ? localStorage : sessionStorage).setItem(TOKEN_KEY, token)
  },
  clear: () => {
    localStorage.removeItem(TOKEN_KEY)
    sessionStorage.removeItem(TOKEN_KEY)
  },
}

const DATABASE_KEY = 'fg.database'

/** Database of the last sign-in (Dev / Prod; the server picks it from the site address), so a switch can reset the selected group. */
export const databaseStore = {
  get: () => localStorage.getItem(DATABASE_KEY),
  set: (key: string) => localStorage.setItem(DATABASE_KEY, key),
}

export const api = axios.create({ baseURL: import.meta.env.BASE_URL.replace(/\/$/, '') + '/api' })

api.interceptors.request.use((config) => {
  const token = tokenStore.get()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  (r) => r,
  (error: AxiosError) => {
    if (error.response?.status === 401 && !error.config?.url?.includes('/auth/login')) {
      tokenStore.clear()
      if (!location.pathname.endsWith('/login')) location.href = import.meta.env.BASE_URL + 'login'
    }
    return Promise.reject(error)
  },
)

/** Extracts the server's `{ message }` (or a generic fallback) from any error. */
export function errorMessage(err: unknown): string {
  const e = err as AxiosError<{ message?: string; title?: string; errors?: Record<string, string[]> }>
  const data = e?.response?.data
  if (data?.message) return data.message
  if (data?.errors) return Object.values(data.errors).flat().join(' ')
  if (data?.title) return data.title
  if (e?.message) return e.message
  return 'Something went wrong.'
}

/** URL for a stored file (images/videos/downloads cannot send the auth header). */
export function fileUrl(path?: string | null, download = false, fileName?: string | null): string {
  if (!path) return ''
  const token = tokenStore.get() ?? ''
  const base = import.meta.env.BASE_URL.replace(/\/$/, '')
  const dl = download ? `&download=1${fileName ? `&name=${encodeURIComponent(fileName)}` : ''}` : ''
  return `${base}/api/files/${path}?access_token=${encodeURIComponent(token)}${dl}`
}

/** Triggers a browser download of an authenticated API response (Excel exports, templates). */
export async function download(url: string, fallbackName: string, params?: Record<string, unknown>) {
  const res = await api.get(url, { params, responseType: 'blob' })
  const disposition = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const name = match ? decodeURIComponent(match[1]) : fallbackName
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = name
  a.click()
  URL.revokeObjectURL(href)
}
