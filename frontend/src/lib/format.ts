import { format, parseISO } from 'date-fns'

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })

export const money = (n?: number | null) => usd.format(Number(n ?? 0))

export function num(n?: number | null, digits = 2) {
  const v = Number(n ?? 0)
  return v.toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: digits })
}

/** Server dates are UTC ISO strings without "Z"; treat them as UTC. */
function toDate(value?: string | null): Date | null {
  if (!value || value.startsWith('0001-')) return null
  const iso = /[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : value + 'Z'
  const d = parseISO(iso)
  return isNaN(d.getTime()) ? null : d
}

export function dateTime(value?: string | null) {
  const d = toDate(value)
  return d ? format(d, 'M/d/yyyy h:mm a') : ''
}

export function dateTimeSeconds(value?: string | null) {
  const d = toDate(value)
  return d ? format(d, 'M/d/yyyy h:mm:ss a') : ''
}

export function date(value?: string | null) {
  const d = toDate(value)
  return d ? format(d, 'MM/dd/yyyy') : ''
}

/** yyyy-MM-dd for <input type="date"> */
export function inputDate(value?: string | null) {
  const d = toDate(value)
  return d ? format(d, 'yyyy-MM-dd') : ''
}

/** Parses user input like "020", "$1,200.50" or "" into a number. */
export function toNumber(v: unknown): number {
  if (typeof v === 'number') return isFinite(v) ? v : 0
  const s = String(v ?? '').replace(/[^0-9.-]/g, '')
  const n = parseFloat(s)
  return isFinite(n) ? n : 0
}

/** (###) ### - #### as in the legacy user form. */
export function formatPhone(input: string) {
  const d = input.replace(/\D/g, '').slice(0, 10)
  if (d.length === 0) return ''
  if (d.length <= 3) return `(${d}`
  if (d.length <= 6) return `(${d.slice(0, 3)}) ${d.slice(3)}`
  return `(${d.slice(0, 3)}) ${d.slice(3, 6)} - ${d.slice(6)}`
}

export const isValidPhone = (s?: string | null) => !s || s.replace(/\D/g, '').length === 10
export const isValidEmail = (s?: string | null) => !!s && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s)
