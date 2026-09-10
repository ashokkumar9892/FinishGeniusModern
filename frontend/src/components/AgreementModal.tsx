import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { api, errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { Checkbox, ErrorBanner, Spinner } from './ui'

/**
 * First-login "Term & Conditions" gate. If the file public/agreement.pdf is deployed it is shown
 * in an embedded viewer; otherwise the summary text below is displayed.
 */
export function AgreementModal() {
  const { refresh, logout } = useAuth()
  const [checked, setChecked] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hasPdf, setHasPdf] = useState(false)
  const pdf = import.meta.env.BASE_URL + 'agreement.pdf'

  useEffect(() => {
    fetch(pdf, { method: 'HEAD' })
      .then((r) => setHasPdf(r.ok && (r.headers.get('content-type') ?? '').includes('pdf')))
      .catch(() => setHasPdf(false))
  }, [pdf])

  const accept = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/auth/accept-agreement')
      await refresh()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/60 backdrop-blur-sm p-4">
      <div className="card w-full max-w-3xl shadow-2xl">
        <div className="border-b-2 border-b-primary/70 px-5 py-3.5">
          <h3 className="text-base font-semibold">Term &amp; Conditions</h3>
        </div>
        <div className="p-5 space-y-4">
          <ErrorBanner message={error} />
          {hasPdf ? (
            <iframe title="User agreement" src={pdf} className="w-full h-[55vh] rounded border" />
          ) : (
            <div className="h-[45vh] overflow-y-auto rounded border bg-muted/40 p-4 text-sm leading-relaxed space-y-3">
              <h4 className="font-semibold">AWFI Agent Finishing &amp; Finish Genius Software Support User Agreement</h4>
              <p>
                This AWFI Agent Finishing &amp; FG Pro+ Software Support Agreement (this "Agreement") is made as of the Effective Date by and
                between AWFI Coating Finishing Solutions, L.L.C., a South Carolina limited liability company with its principal place of
                business at 8334 Pineville Matthews Rd, Ste 103-159 Charlotte, NC 28226 ("AWFI"), and the Users of the Finish Genius software.
              </p>
              <p>
                By using Finish Genius you agree to use the software and its content only for your organization's finishing, formulation and
                process management, to keep your login credentials confidential, and to follow the safety data sheets and technical data
                supplied with each material.
              </p>
              <p>
                Process, pricing and quantity outputs are estimates based on the information you enter. You remain responsible for verifying
                results before production use.
              </p>
              <p className="text-muted-foreground">(Your administrator can publish the full agreement by placing agreement.pdf in the site root.)</p>
            </div>
          )}
          <Checkbox
            checked={checked}
            onChange={setChecked}
            label="By checking this box, I confirm that I have read, understood, and agree to the terms and conditions outlined in the document provided"
          />
        </div>
        <div className="flex justify-end gap-2 border-t px-5 py-3 bg-muted/40 rounded-b-lg">
          <button className="btn-secondary" onClick={logout}>
            Sign out
          </button>
          <button className="btn-primary" disabled={!checked || busy} onClick={accept}>
            {busy && <Spinner />} Accept
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
