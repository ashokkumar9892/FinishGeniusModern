import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'

/**
 * Content that is invisible on screen and is the ONLY thing printed while mounted
 * (the app root and modals are hidden in print). Mount one at a time.
 */
export function PrintArea({ children }: { children: ReactNode }) {
  return createPortal(
    <div className="fg-print-area">
      <style>{`
        .fg-print-area { display: none; }
        @media print {
          #root { display: none !important; }
          .fg-print-area { display: block !important; color: #000; background: #fff; }
          .fg-print-area table { width: 100%; border-collapse: collapse; }
          .fg-print-area th, .fg-print-area td { border-bottom: 1px solid #ddd; padding: 4px 6px; text-align: left; }
          @page { margin: 12mm; }
        }
      `}</style>
      {children}
    </div>,
    document.body,
  )
}
