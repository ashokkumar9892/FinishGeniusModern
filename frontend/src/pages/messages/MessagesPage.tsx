import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CheckCheck, Inbox, Mail, MailOpen, PenSquare, Reply, Send, Trash2 } from 'lucide-react'
import { format } from 'date-fns'
import clsx from 'clsx'
import { api, errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { dateTime } from '@/lib/format'
import { ConfirmDialog, EmptyState, ErrorBanner, LoadingBlock, PageHeader, SearchInput, Spinner, Tabs } from '@/components/ui'
import { useToast } from '@/components/toast'
import { ComposeModal } from './ComposeModal'
import { isMessageType, TYPE_STYLES, type MessageListItem, type MessageType, type Thread } from './types'

type Box = 'inbox' | 'sent'

function toDate(v: string) {
  const d = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(v) ? v : v + 'Z')
  return isNaN(d.getTime()) ? null : d
}

/** "3:41 PM" today, "Sep 4" this year, "9/4/25" otherwise. */
function shortDate(v: string) {
  const d = toDate(v)
  if (!d) return ''
  const now = new Date()
  if (d.toDateString() === now.toDateString()) return format(d, 'h:mm a')
  return d.getFullYear() === now.getFullYear() ? format(d, 'MMM d') : format(d, 'M/d/yy')
}

function TypeBadge({ type, label }: { type: string; label: string }) {
  return <span className={clsx('badge whitespace-nowrap', TYPE_STYLES[type] ?? 'bg-muted')}>{label}</span>
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase() || '?'
}

export default function MessagesPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const { refresh, me } = useAuth()
  const [params, setParams] = useSearchParams()

  const [box, setBox] = useState<Box>('inbox')
  const [search, setSearch] = useState('')
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [compose, setCompose] = useState<{ key: number; type: MessageType; subject: string } | null>(null)
  const [deleting, setDeleting] = useState<number | null>(null)

  // Contract used by other modules: /messages?compose=Support&subject=My%20Work%20%2327347
  useEffect(() => {
    const c = params.get('compose')
    if (c === null) return
    const match = ['General', 'Support', 'Question', 'Internal'].find((t) => t.toLowerCase() === c.toLowerCase())
    setCompose({ key: Date.now(), type: isMessageType(match) ? match : 'General', subject: params.get('subject') ?? '' })
    const next = new URLSearchParams(params)
    next.delete('compose')
    next.delete('subject')
    setParams(next, { replace: true })
  }, [params, setParams])

  const list = useQuery({
    queryKey: ['messages', box],
    queryFn: () => api.get<MessageListItem[]>(`/messages/${box}`).then((r) => r.data),
    refetchInterval: 60_000,
  })

  const thread = useQuery({
    queryKey: ['message', selectedId],
    queryFn: () => api.get<Thread>(`/messages/${selectedId}`).then((r) => r.data),
    enabled: selectedId != null,
  })

  // Opening a conversation marks it read: refresh the inbox and the header badge.
  const lastMarked = useRef<string>('')
  useEffect(() => {
    const t = thread.data
    if (!t || t.markedRead === 0) return
    const key = `${t.id}:${thread.dataUpdatedAt}`
    if (lastMarked.current === key) return
    lastMarked.current = key
    const ids = new Set(t.messages.map((m) => m.id))
    qc.setQueryData<MessageListItem[]>(['messages', 'inbox'], (rows) => rows?.map((r) => (ids.has(r.id) ? { ...r, viewed: true } : r)))
    refresh()
  }, [thread.data, thread.dataUpdatedAt, qc, refresh])

  const filtered = useMemo(() => {
    const s = search.trim().toLowerCase()
    const rows = list.data ?? []
    if (!s) return rows
    return rows.filter((m) => [m.subject, m.snippet, m.fromName, m.groupName, m.typeLabel, ...(m.toNames ?? [])].join(' ').toLowerCase().includes(s))
  }, [list.data, search])

  const unreadCount = me?.unreadMessages ?? 0

  const markAll = useMutation({
    mutationFn: () => api.post('/messages/mark-all-read'),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['messages'] })
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const markUnread = useMutation({
    mutationFn: (id: number) => api.post(`/messages/${id}/unread`),
    onSuccess: async () => {
      setSelectedId(null)
      await qc.invalidateQueries({ queryKey: ['messages'] })
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const remove = useMutation({
    mutationFn: (id: number) => api.delete(`/messages/${id}`),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      setDeleting(null)
      setSelectedId(null)
      await qc.invalidateQueries({ queryKey: ['messages'] })
      refresh()
    },
    onError: (e) => {
      setDeleting(null)
      toast.error(errorMessage(e))
    },
  })

  const openCompose = (type: MessageType = 'General', subject = '') => setCompose({ key: Date.now(), type, subject })

  return (
    <>
      <PageHeader
        title="DPM Center"
        breadcrumbs={['DPM Center']}
        subtitle="Messages with your team and AWFI support"
        actions={
          <>
            <button className="btn-secondary" onClick={() => markAll.mutate()} disabled={markAll.isPending || unreadCount === 0}>
              {markAll.isPending ? <Spinner /> : <CheckCheck className="h-4 w-4" />} Mark all read
            </button>
            <button className="btn-primary" onClick={() => openCompose()}>
              <PenSquare className="h-4 w-4" /> New message
            </button>
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-[minmax(300px,400px)_minmax(0,1fr)] lg:h-[calc(100vh-13.5rem)] lg:min-h-[520px]">
        {/* List */}
        <section className={clsx('card flex flex-col min-h-0 overflow-hidden', selectedId != null && 'hidden lg:flex')}>
          <Tabs
            className="px-2"
            value={box}
            onChange={(b) => {
              setBox(b)
              setSelectedId(null)
            }}
            tabs={[
              { key: 'inbox', label: 'Inbox', count: unreadCount || undefined },
              { key: 'sent', label: 'Sent' },
            ]}
          />
          <div className="p-2 border-b">
            <SearchInput value={search} onChange={setSearch} placeholder={`Search ${box}…`} />
          </div>
          <div className="flex-1 min-h-0 overflow-y-auto max-h-[70vh] lg:max-h-none">
            {list.isLoading ? (
              <LoadingBlock />
            ) : list.isError ? (
              <div className="p-3"><ErrorBanner message={errorMessage(list.error)} /></div>
            ) : filtered.length === 0 ? (
              <EmptyState
                icon={box === 'inbox' ? <Inbox className="h-5 w-5" /> : <Send className="h-5 w-5" />}
                title={search ? 'No matching messages' : box === 'inbox' ? 'Your inbox is empty' : 'No sent messages'}
                description={search ? undefined : box === 'inbox' ? 'Messages from your team and AWFI support will appear here.' : 'Messages you send will appear here.'}
              />
            ) : (
              <ul className="divide-y">
                {filtered.map((m) => {
                  const unread = box === 'inbox' && !m.viewed
                  const who = box === 'inbox'
                    ? m.fromName
                    : `To: ${(m.toNames ?? []).join(', ')}${(m.recipientCount ?? 0) > (m.toNames?.length ?? 0) ? ` +${(m.recipientCount ?? 0) - (m.toNames?.length ?? 0)}` : ''}`
                  return (
                    <li key={m.id}>
                      <button
                        className={clsx(
                          'w-full text-left px-3 py-2.5 flex gap-2.5 transition-colors',
                          selectedId === m.id ? 'bg-accent' : 'hover:bg-muted/50',
                        )}
                        onClick={() => setSelectedId(m.id)}
                      >
                        <span className="w-2 pt-1.5 shrink-0">
                          {unread && <span className="block h-2 w-2 rounded-full bg-primary" aria-label="Unread" />}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="flex items-baseline gap-2">
                            <span className={clsx('truncate flex-1 text-sm', unread ? 'font-semibold' : 'text-foreground/90')}>{who}</span>
                            <span className={clsx('text-xs shrink-0', unread ? 'text-primary font-medium' : 'text-muted-foreground')}>{shortDate(m.sentAt)}</span>
                          </span>
                          <span className={clsx('block truncate text-sm', unread ? 'font-semibold' : '')}>{m.subject}</span>
                          <span className="block truncate text-xs text-muted-foreground">{m.snippet}</span>
                          <span className="mt-1 flex items-center gap-1.5">
                            <TypeBadge type={m.type} label={m.typeLabel} />
                            {m.groupName && <span className="text-[11px] text-muted-foreground truncate">{m.groupName}</span>}
                          </span>
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
          {!list.isLoading && (list.data?.length ?? 0) > 0 && (
            <div className="px-3 py-1.5 border-t text-xs text-muted-foreground">
              {filtered.length === list.data!.length ? `${list.data!.length} messages` : `${filtered.length} of ${list.data!.length} messages`}
            </div>
          )}
        </section>

        {/* Reading pane */}
        <section className={clsx('card flex flex-col min-h-0 overflow-hidden', selectedId == null && 'hidden lg:flex')}>
          {selectedId == null ? (
            <div className="flex-1 grid place-items-center">
              <EmptyState
                icon={<Mail className="h-5 w-5" />}
                title="Select a message to read"
                description="Or start a new conversation with your team or AWFI support."
                action={<button className="btn-primary" onClick={() => openCompose()}><PenSquare className="h-4 w-4" /> New message</button>}
              />
            </div>
          ) : thread.isLoading ? (
            <LoadingBlock />
          ) : thread.isError || !thread.data ? (
            <div className="p-4">
              <button className="btn-ghost btn-sm mb-2 lg:hidden" onClick={() => setSelectedId(null)}><ArrowLeft className="h-4 w-4" /> Back</button>
              <ErrorBanner message={errorMessage(thread.error)} />
            </div>
          ) : (
            <ThreadView
              thread={thread.data}
              canManage={box === 'inbox'}
              onBack={() => setSelectedId(null)}
              onUnread={() => markUnread.mutate(selectedId)}
              onDelete={() => setDeleting(selectedId)}
            />
          )}
        </section>
      </div>

      {compose && (
        <ComposeModal
          key={compose.key}
          initialType={compose.type}
          initialSubject={compose.subject}
          onClose={() => setCompose(null)}
          onSent={(id) => {
            setCompose(null)
            setBox('sent')
            setSelectedId(id)
          }}
        />
      )}
      <ConfirmDialog
        open={deleting != null}
        message="Are you sure you want to delete this message from your inbox?"
        busy={remove.isPending}
        onConfirm={() => deleting != null && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}

function ThreadView({ thread, canManage, onBack, onUnread, onDelete }: {
  thread: Thread
  canManage: boolean
  onBack: () => void
  onUnread: () => void
  onDelete: () => void
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [reply, setReply] = useState('')
  const [replyOpen, setReplyOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const bottom = useRef<HTMLDivElement>(null)
  const last = thread.messages[thread.messages.length - 1]

  useEffect(() => {
    setReply('')
    setReplyOpen(false)
    setError(null)
  }, [thread.id])

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' })
  }, [thread.id, thread.messages.length])

  const send = useMutation({
    mutationFn: () => api.post('/messages', { groupId: thread.groupId, replyToId: last.id, body: reply.trim(), recipientUserIds: [] }),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      setReply('')
      setReplyOpen(false)
      await qc.invalidateQueries({ queryKey: ['message'] })
      qc.invalidateQueries({ queryKey: ['messages'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const replyTo = last.isMine ? last.recipients.map((r) => r.name).join(', ') : last.fromName

  return (
    <>
      <div className="flex items-start gap-2 border-b px-4 py-3">
        <button className="btn-icon lg:hidden -ml-1" onClick={onBack} aria-label="Back to list">
          <ArrowLeft className="h-4 w-4" />
        </button>
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold text-base break-words">{thread.subject}</h2>
          <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <TypeBadge type={thread.type} label={thread.typeLabel} />
            {thread.groupName && <span>{thread.groupName}</span>}
            {thread.messages.length > 1 && <span>· {thread.messages.length} messages</span>}
          </div>
        </div>
        <div className="flex gap-0.5 shrink-0">
          <button className="btn-icon" title="Reply" onClick={() => setReplyOpen(true)}>
            <Reply className="h-4 w-4" />
          </button>
          {canManage && (
            <>
              <button className="btn-icon" title="Mark as unread" onClick={onUnread}>
                <MailOpen className="h-4 w-4" />
              </button>
              <button className="btn-icon hover:text-destructive" title="Delete" onClick={onDelete}>
                <Trash2 className="h-4 w-4" />
              </button>
            </>
          )}
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto px-4 py-3 space-y-3 bg-muted/20">
        {thread.messages.map((m) => (
          <article key={m.id} className={clsx('rounded-lg border bg-card p-3 sm:p-4', m.isMine && 'border-primary/30')}>
            <header className="flex items-start gap-3">
              <span className={clsx('h-9 w-9 shrink-0 rounded-full grid place-items-center text-xs font-bold', m.isMine ? 'bg-primary text-primary-foreground' : 'bg-primary/15 text-primary')}>
                {initials(m.fromName)}
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                  <span className="font-medium text-sm">
                    {m.isMine ? 'You' : m.fromName}
                    {!m.isMine && <span className="font-normal text-muted-foreground"> &lt;{m.fromEmail}&gt;</span>}
                  </span>
                  <span className="text-xs text-muted-foreground" title={dateTime(m.sentAt)}>{dateTime(m.sentAt)}</span>
                </div>
                <div className="text-xs text-muted-foreground truncate">To: {m.recipients.map((r) => r.name).join(', ') || '—'}</div>
              </div>
            </header>
            <div className="mt-3 text-sm whitespace-pre-wrap break-words leading-relaxed">{m.body}</div>
          </article>
        ))}
        <div ref={bottom} />
      </div>

      <div className="border-t p-3">
        {replyOpen ? (
          <div className="space-y-2">
            <ErrorBanner message={error} />
            <div className="text-xs text-muted-foreground truncate">Reply to: {replyTo}</div>
            <textarea
              className="input"
              rows={4}
              autoFocus
              value={reply}
              maxLength={20000}
              placeholder="Write your reply…"
              onChange={(e) => setReply(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && reply.trim()) send.mutate()
              }}
            />
            <div className="flex justify-end gap-2">
              <button className="btn-secondary btn-sm" onClick={() => { setReplyOpen(false); setError(null) }} disabled={send.isPending}>Cancel</button>
              <button className="btn-primary btn-sm" onClick={() => send.mutate()} disabled={send.isPending || !reply.trim()}>
                {send.isPending ? <Spinner className="h-3.5 w-3.5" /> : <Send className="h-3.5 w-3.5" />} Send reply
              </button>
            </div>
          </div>
        ) : (
          <button className="btn-secondary w-full sm:w-auto" onClick={() => setReplyOpen(true)}>
            <Reply className="h-4 w-4" /> Reply
          </button>
        )}
      </div>
    </>
  )
}
