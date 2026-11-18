import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { api, getStoredApiKey, setStoredApiKey, type FrontendSettings, type Language } from './api/client'
import { Header, type Page } from './components/Header'
import { useI18n } from './lib/i18n'
import { AdminPage } from './pages/AdminPage'
import { TranslatePage } from './pages/TranslatePage'

function pageFromHash(): Page {
  return window.location.hash === '#/admin' ? 'admin' : 'translate'
}

export default function App() {
  const { t } = useI18n()
  const [page, setPage] = useState<Page>(pageFromHash)
  const [settings, setSettings] = useState<FrontendSettings | null>(null)
  const [languages, setLanguages] = useState<Language[]>([])
  const [error, setError] = useState('')
  const [showKey, setShowKey] = useState(false)
  const [apiKey, setApiKey] = useState(getStoredApiKey)

  useEffect(() => {
    Promise.all([api.settings(), api.languages()])
      .then(([s, l]) => {
        setSettings(s)
        setLanguages(l)
        if (s.keyRequired && !getStoredApiKey()) setShowKey(true)
      })
      .catch(() => setError(t('loadError')))
  }, [t])

  useEffect(() => {
    const onHash = () => setPage(pageFromHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const navigate = (p: Page) => {
    window.location.hash = p === 'admin' ? '#/admin' : ''
    setPage(p)
  }

  return (
    <div className="flex min-h-full flex-col">
      <Header page={page} onNavigate={navigate} showKeyButton={!!settings?.apiKeys} onToggleKey={() => setShowKey((v) => !v)} />

      {showKey && (
        <div className="border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <form
            className="mx-auto flex max-w-6xl flex-wrap items-center gap-2 px-4 py-3"
            onSubmit={(e) => {
              e.preventDefault()
              setStoredApiKey(apiKey.trim())
              setShowKey(false)
            }}
          >
            <span className="text-sm font-medium">{t('apiKey')}</span>
            <input
              className="input max-w-sm font-mono"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            />
            <button className="btn-primary">{t('save')}</button>
            <span className="text-xs text-slate-500">{t('apiKeyHint')}</span>
          </form>
        </div>
      )}

      <main className="flex-1">
        {page === 'admin' ? (
          <AdminPage />
        ) : error ? (
          <div className="mx-auto max-w-6xl px-4 py-10 text-center text-red-600">{error}</div>
        ) : !settings ? (
          <div className="grid place-items-center py-20">
            <Loader2 className="animate-spin text-slate-400" />
          </div>
        ) : languages.length === 0 ? (
          <div className="mx-auto max-w-2xl px-4 py-10">
            <div className="card p-6 text-center text-amber-700 dark:text-amber-400">{t('noModels')}</div>
          </div>
        ) : (
          <TranslatePage settings={settings} languages={languages} />
        )}
      </main>

      <footer className="py-6 text-center text-xs text-slate-500">
        NetTranslate · ASP.NET Core .NET 10 · ONNX Runtime · MarianMT (Helsinki-NLP OPUS-MT)
      </footer>
    </div>
  )
}
