import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Download, Upload } from 'lucide-react'
import { errorMessage } from '@/lib/api'
import { FileDrop } from '@/components/FileDrop'
import { ErrorBanner, Field, Modal, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { ImportResults } from './ImportResults'
import { documentExtensions, downloadImportTemplate, uploadMaterials, type ImportResult } from './shared'

/** "Blk Upld" — Bulk Material Excel Upload (same template as the Import page). */
export function BulkUploadModal({ open, onClose, groupId, groupName }: { open: boolean; onClose: () => void; groupId: number; groupName?: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [file, setFile] = useState<File[]>([])
  const [docs, setDocs] = useState<File[]>([])
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [downloading, setDownloading] = useState(false)

  useEffect(() => {
    if (open) {
      setFile([])
      setDocs([])
      setError(null)
      setResult(null)
    }
  }, [open])

  const upload = useMutation({
    mutationFn: () => uploadMaterials(groupId, file[0], docs),
    onSuccess: (res) => {
      setResult(res)
      setError(null)
      if (res.created > 0) toast.success(res.message)
      qc.invalidateQueries({ queryKey: ['materials'] })
      qc.invalidateQueries({ queryKey: ['material-categories'] })
      qc.invalidateQueries({ queryKey: ['documents'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const sample = async () => {
    setDownloading(true)
    try {
      await downloadImportTemplate()
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setDownloading(false)
    }
  }

  const submit = () => {
    if (!file[0]) {
      setError('Please choose an Excel (.xlsx) file to upload.')
      return
    }
    setResult(null)
    upload.mutate()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Bulk Material Excel Upload"
      size="lg"
      footer={
        <>
          <button className="btn-secondary mr-auto" onClick={sample} disabled={downloading}>
            {downloading ? <Spinner /> : <Download className="h-4 w-4" />} Download Sample
          </button>
          <button className="btn-secondary" onClick={onClose} disabled={upload.isPending}>
            {result ? 'Close' : 'Cancel'}
          </button>
          <button className="btn-primary" onClick={submit} disabled={upload.isPending || !file[0]}>
            {upload.isPending ? <Spinner /> : <Upload className="h-4 w-4" />} Upload
          </button>
        </>
      }
    >
      <ErrorBanner message={error} />
      {groupName && (
        <p className="mb-3 text-sm text-muted-foreground">
          Materials will be added to the <span className="font-medium text-foreground">{groupName}</span> group. Each sheet (Base Materials, Pigment
          Materials, Dye Materials, Sundry Materials, Equipment) sets the material type.
        </p>
      )}
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Browse File" required hint="Excel .xlsx in the sample template format.">
          <FileDrop
            files={file}
            onChange={(f) => {
              setFile(f)
              setResult(null)
            }}
            accept={['xlsx']}
            label="Drag & drop the Excel file here …"
          />
        </Field>
        <Field label="Browse Documents" hint='Optional — matched to the "Document File" column by file name.'>
          <FileDrop files={docs} onChange={setDocs} accept={documentExtensions} multiple label="Drag & drop related documents …" />
        </Field>
      </div>
      {result && (
        <div className="mt-4">
          <ImportResults result={result} />
        </div>
      )}
    </Modal>
  )
}
