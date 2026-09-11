import { api } from '@/lib/api'
import type { CommandStatus } from './types'

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

/**
 * Polls GET /api/dispensing/commands/{id} until the network bridge answered (or `timeoutMs` passed).
 * Hardware jobs (dispense, scale, label, purge) are queued for the bridge; see backend DeviceCommandService.
 */
export async function waitForCommand(
  id: number,
  { timeoutMs = 30_000, intervalMs = 1000, onUpdate, isCancelled }: {
    timeoutMs?: number
    intervalMs?: number
    onUpdate?: (s: CommandStatus) => void
    isCancelled?: () => boolean
  } = {},
): Promise<CommandStatus> {
  const started = Date.now()
  for (;;) {
    const { data } = await api.get<CommandStatus>(`/dispensing/commands/${id}`)
    onUpdate?.(data)
    if (data.done || isCancelled?.()) return data
    if (Date.now() - started > timeoutMs) {
      return { ...data, done: true, status: 'Expired', message: 'No response received from the network bridge.' }
    }
    await sleep(intervalMs)
  }
}

/** Reads the weight (grams) of a scale through its network bridge; throws with the legacy message on failure. */
export async function readScaleWeight(deviceId: number): Promise<number> {
  const { data } = await api.post<{ commandId: number }>(`/dispensing/scales/${deviceId}/weight`)
  const s = await waitForCommand(data.commandId, { timeoutMs: 35_000 })
  if (s.status === 'Succeeded' && s.weightGrams && s.weightGrams > 0) return s.weightGrams
  if (!s.done || s.status === 'Expired') api.post(`/dispensing/commands/${data.commandId}/cancel`).catch(() => undefined)
  throw new Error(s.message || 'Network bridge could not detect scale.')
}

/** Tares a scale (fire and forget after a weight was taken, like the legacy page). */
export async function tareScale(deviceId: number): Promise<void> {
  const { data } = await api.post<{ commandId: number }>(`/dispensing/scales/${deviceId}/tare`)
  const s = await waitForCommand(data.commandId, { timeoutMs: 35_000 })
  if (s.status !== 'Succeeded') throw new Error(s.message || 'Unable to tare the scale.')
}
