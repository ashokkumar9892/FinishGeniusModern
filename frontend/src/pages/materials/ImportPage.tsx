import { useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { FileSpreadsheet, Plus } from 'lucide-react'
import { errorMessage } from '@/lib/api'
import { useGroup } from '@/lib/auth'
import { FileDrop } from '@/components/FileDrop'
import { SearchSelect } from '@/components/SearchSelect'
import { Card, ErrorBanner, Field, PageHeader, Spinner } from '@/components/ui'
import { useToast } from '@/components/toast'
import { ImportResults } from './ImportResults'
import { documentExtensions, downloadImportTemplate, uploadMaterials, type ImportResult } from './shared'

const sheets = ['Base Materials', 'Pigment Materials', 'Dye Materials', 'Sundry Materials', 'Equipment']
const headers = ['Material Category', 'Product Name', 'Product Rex #', 'Lb/Gal', 'Price Per Gallon/Pc.', "VOC's", "Hap's", "TAP's", 'Document File']

export default function ImportPage() {
  const qc = useQueryClient()
  const toast = useToast()
  const { groupId: currentGroup, groups } = useGroup()
  const [groupId, setGroupId] = useState<number>(currentGroup)
  const [file, setFile] = useState<File | null>(null)
  const [docs, setDocs] = useState<File[]>([])
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const input = useRef<HTMLInputElement>(null)

  const upload = useMutation({
    mutationFn: () => uploadMaterials(groupId, file!, docs),
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

  const template = async () => {
    try {
      await downloadImportTemplate()
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  const pick = (f: File | null) => {
    setResult(null)
    if (f && !f.name.toLowerCase().endsWith('.xlsx')) {
      setFile(null)
      setError(`Invalid file "${f.name}". Only Excel .xlsx files are supported.`)
      if (input.current) input.current.value = ''
      return
    }
    setError(null)
    setFile(f)
  }

  const submit = () => {
    if (!groupId) return setError('Please select a group.')
    if (!file) return setError('Please choose an Excel (.xlsx) file to import.')
    setResult(null)
    upload.mutate()
  }

  return (
    <>
      <PageHeader title="Import Materials" breadcrumbs={['Import']} />
      <div className="grid gap-5 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card title="Import Materials">
          <p className="text-sm mb-4">
            You can download a template file from{' '}
            <button type="button" className="font-medium text-primary hover:underline" onClick={template}>
              here
            </button>
            .
          </p>
          <ErrorBanner message={error} />
          <div className="space-y-4">
            <Field label="Group" required>
              <SearchSelect options={groups.map((g) => ({ value: g.id, label: g.name }))} value={groupId} clearable={false} onChange={(v) => v && setGroupId(v)} />
            </Field>
            <Field label="Excel file" required hint="The .xlsx template: one sheet per material type, headers in row 1.">
              <input
                ref={input}
                type="file"
                accept=".xlsx"
                className="block w-full text-sm file:mr-3 file:rounded-md file:border-0 file:bg-primary file:px-3 file:py-2 file:text-sm file:font-medium file:text-primary-foreground hover:file:bg-primary/90 file:cursor-pointer rounded-md border border-input bg-card"
                onChange={(e) => pick(e.target.files?.[0] ?? null)}
              />
            </Field>
            <Field label="Documents (optional)" hint='Matched to the "Document File" column by file name and linked to the imported materials.'>
              <FileDrop files={docs} onChange={setDocs} accept={documentExtensions} multiple label="Drag & drop related documents …" />
            </Field>
            <div className="flex justify-end">
              <button className="btn-primary" onClick={submit} disabled={upload.isPending || !file}>
                {upload.isPending ? <Spinner /> : <Plus className="h-4 w-4" />} Import
              </button>
            </div>
          </div>
          {result && (
            <div className="mt-5 border-t pt-5">
              <ImportResults result={result} />
            </div>
          )}
        </Card>

        <Card
          title={
            <span className="flex items-center gap-2">
              <FileSpreadsheet className="h-4 w-4 text-success" /> Template format
            </span>
          }
        >
          <div className="space-y-3 text-sm">
            <div>
              <div className="label">Sheets (sheet name sets the material type)</div>
              <div className="flex flex-wrap gap-1.5">
                {sheets.map((s) => (
                  <span key={s} className="badge bg-muted text-foreground">
                    {s}
                  </span>
                ))}
              </div>
            </div>
            <div>
              <div className="label">Row 1 headers</div>
              <ol className="list-decimal pl-5 text-muted-foreground space-y-0.5">
                {headers.map((h) => (
                  <li key={h}>
                    <span className="text-foreground">{h}</span>
                  </li>
                ))}
              </ol>
            </div>
            <ul className="list-disc pl-5 text-xs text-muted-foreground space-y-1">
              <li>Product Name is required; blank rows are skipped.</li>
              <li>Numbers may include “$”, commas or trailing tabs; blank numbers are 0.</li>
              <li>Categories are matched by name within the material type and created when missing.</li>
              <li>Materials that already exist (same type, name and product #) are skipped.</li>
              <li>VOC’s / Hap’s / TAP’s are lb per gallon (ignored for Equipment).</li>
            </ul>
          </div>
        </Card>
      </div>
    </>
  )
}
