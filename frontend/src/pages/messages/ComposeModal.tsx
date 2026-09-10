import { useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Send } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useGroup, useMe } from '@/lib/auth'
import { isSystemAdmin } from '@/lib/access'
import { ErrorBanner, Field, Modal, Note, Spinner } from '@/components/ui'
import { SearchSelect } from '@/components/SearchSelect'
import { useToast } from '@/components/toast'
import { MultiSelect } from '@/pages/users/MultiSelect'
import { MESSAGE_TYPES, TYPE_LABELS, type MessageType, type Recipient } from './types'

const TYPE_NOTES: Partial<Record<MessageType, string>> = {
  Support: 'Your message will be delivered to the AWFI FG APP support team.',
  Question: 'Your finishing question will be delivered to the AWFI finishing experts.',
  Internal: 'Internal DPM messages are delivered to all System Administrators.',
}

export function ComposeModal({ initialType, initialSubject, onClose, onSent }: {
  initialType: MessageType
  initialSubject?: string
  onClose: () => void
  onSent: (id: number) => void
}) {
  const me = useMe()
  const { groupId: currentGroup, groups } = useGroup()
  const qc = useQueryClient()
  const toast = useToast()
  const sysAdmin = isSystemAdmin(me)

  const [type, setType] = useState<MessageType>(initialType === 'Internal' && !sysAdmin ? 'Support' : initialType)
  const [groupId, setGroupId] = useState<number | null>(currentGroup)
  const [recipients, setRecipients] = useState<number[]>([])
  const [subject, setSubject] = useState(initialSubject ?? '')
  const [body, setBody] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const needsRecipients = type === 'General'
  const recipientsQuery = useQuery({
    queryKey: ['message-recipients', groupId],
    queryFn: () => api.get<Recipient[]>('/messages/recipients', { params: { groupId } }).then((r) => r.data),
    enabled: needsRecipients && !!groupId,
    staleTime: 60_000,
  })
  const recipientOptions = useMemo(
    () => (recipientsQuery.data ?? []).map((r) => ({ value: r.id, label: r.name, sub: r.inGroup ? r.email : `${r.email} · ${r.groupName ?? ''}`, tag: r.tag })),
    [recipientsQuery.data],
  )

  const errors = {
    groupId: !groupId ? 'Group is required.' : null,
    recipients: needsRecipients && recipients.length === 0 ? 'Select at least one recipient.' : null,
    subject: !subject.trim() ? 'Subject is required.' : null,
    body: !body.trim() ? 'Message is required.' : null,
  }
  const show = (k: keyof typeof errors) => (submitted ? errors[k] : null)

  const send = useMutation({
    mutationFn: () =>
      api.post<{ message: string; id: number }>('/messages', {
        groupId,
        type,
        subject: subject.trim(),
        body: body.trim(),
        recipientUserIds: needsRecipients ? recipients : [],
      }),
    onSuccess: async (res) => {
      toast.success(res.data.message)
      await qc.invalidateQueries({ queryKey: ['messages'] })
      onSent(res.data.id)
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    if (Object.values(errors).some(Boolean)) return
    send.mutate()
  }

  const types = MESSAGE_TYPES.filter((t) => t !== 'Internal' || sysAdmin)
  const bannerList = submitted ? Object.values(errors).filter(Boolean) : []

  return (
    <Modal
      open
      onClose={() => !send.isPending && onClose()}
      title="New message"
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={send.isPending}>Cancel</button>
          <button className="btn-primary" type="submit" form="compose-form" disabled={send.isPending}>
            {send.isPending ? <Spinner /> : <Send className="h-4 w-4" />} Send
          </button>
        </>
      }
    >
      <form id="compose-form" onSubmit={submit} noValidate className="space-y-4">
        <ErrorBanner message={error ?? (bannerList.length ? bannerList.join('\n') : null)} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Message type" required>
            <select className="input" value={type} onChange={(e) => setType(e.target.value as MessageType)}>
              {types.map((t) => (
                <option key={t} value={t}>{TYPE_LABELS[t]}</option>
              ))}
            </select>
          </Field>
          <Field label="Group" required error={show('groupId')}>
            <SearchSelect
              options={groups.map((g) => ({ value: g.id, label: g.name }))}
              value={groupId}
              onChange={(v) => {
                setGroupId(v)
                setRecipients([])
              }}
              clearable={false}
              invalid={!!show('groupId')}
            />
          </Field>
        </div>
        {needsRecipients ? (
          <Field label="To" required error={show('recipients')}>
            <MultiSelect
              options={recipientOptions}
              value={recipients}
              onChange={setRecipients}
              invalid={!!show('recipients')}
              placeholder={recipientsQuery.isLoading ? 'Loading recipients…' : 'Select recipients'}
              emptyText={recipientsQuery.isError ? errorMessage(recipientsQuery.error) : 'No users found'}
            />
          </Field>
        ) : (
          TYPE_NOTES[type] && <Note tone="info">{TYPE_NOTES[type]}</Note>
        )}
        <Field label="Subject" required error={show('subject')}>
          <input className={show('subject') ? 'input input-invalid' : 'input'} value={subject} maxLength={400} onChange={(e) => setSubject(e.target.value)} autoFocus={!initialSubject} />
        </Field>
        <Field label="Message" required error={show('body')}>
          <textarea
            className={show('body') ? 'input input-invalid' : 'input'}
            rows={8}
            value={body}
            maxLength={20000}
            onChange={(e) => setBody(e.target.value)}
            autoFocus={!!initialSubject}
            placeholder={type === 'Question' ? 'Describe the substrate, coating and the issue you are seeing…' : 'Write your message…'}
          />
        </Field>
      </form>
    </Modal>
  )
}
