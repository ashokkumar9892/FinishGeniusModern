export const MESSAGE_TYPES = ['General', 'Support', 'Question', 'Internal'] as const
export type MessageType = (typeof MESSAGE_TYPES)[number]

export const TYPE_LABELS: Record<MessageType, string> = {
  General: 'General message',
  Support: 'FG APP Support',
  Question: 'Finishing Questions',
  Internal: 'Internal DPM',
}

export const TYPE_STYLES: Record<string, string> = {
  General: 'bg-slate-100 text-slate-700',
  Support: 'bg-orange-100 text-orange-800',
  Question: 'bg-sky-100 text-sky-800',
  Internal: 'bg-violet-100 text-violet-800',
}

export interface MessageListItem {
  id: number
  groupId: number
  groupName?: string | null
  fromUserId?: number
  fromName?: string
  subject: string
  snippet: string
  type: MessageType
  typeLabel: string
  replyToId?: number | null
  sentAt: string
  viewed: boolean
  toNames?: string[]
  recipientCount?: number
}

export interface ThreadMessage {
  id: number
  fromUserId: number
  fromName: string
  fromEmail: string
  subject: string
  body: string
  replyToId?: number | null
  sentAt: string
  isMine: boolean
  recipients: { id: number; name: string; viewed: boolean }[]
}

export interface Thread {
  id: number
  subject: string
  type: MessageType
  typeLabel: string
  groupId: number
  groupName?: string | null
  markedRead: number
  messages: ThreadMessage[]
}

export interface Recipient {
  id: number
  name: string
  email: string
  groupName?: string | null
  tag?: string | null
  inGroup: boolean
}

export function isMessageType(v: string | null | undefined): v is MessageType {
  return !!v && (MESSAGE_TYPES as readonly string[]).includes(v)
}
