import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '@/lib/auth'
import { applyAppearance, storedAppearance } from '@/lib/appearance'
import { preloadRoute } from '@/lib/preload'
import { ToastProvider } from '@/components/toast'
import App from './App'
import './index.css'

// The chosen background and palette, on every screen including sign-in.
applyAppearance(storedAppearance(), false)

// Fetch the opened page's own code alongside the session check instead of after it.
preloadRoute(location.pathname)

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { refetchOnWindowFocus: false, retry: 1, staleTime: 15_000 },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename={import.meta.env.BASE_URL}>
        <ToastProvider>
          <AuthProvider>
            <App />
          </AuthProvider>
        </ToastProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
