import { useCallback, useEffect, useRef, useState } from 'react'
import { Copy, Download, Loader2, LogOut, Plus, RefreshCw, Trash2, X } from 'lucide-react'
import {
  adminApi,
  ApiError,
  type ApiKeyDto,
  type ModelDownloadRequest,
  type ModelDownloadState,
  type ModelDownloadStatus,
  type ModelInfo,
} from '../api/client'
import { useI18n, type TKey } from '../lib/i18n'

const TOKEN_KEY = 'nt-admin-token'

function readToken() {
  try {
    return sessionStorage.getItem(TOKEN_KEY) ?? ''
  } catch {
    return ''
  }
}

export function AdminPage() {
  const { t } = useI18n()
  const [token, setToken] = useState(readToken)
  const [input, setInput] = useState('')
  const [tab, setTab] = useState<'keys' | 'models'>('keys')

  const logout = () => {
    try {
      sessionStorage.removeItem(TOKEN_KEY)
    } catch {
      /* ignore */
    }
    setToken('')
  }

  if (!token) {
    return (
      <div className="mx-auto max-w-sm px-4 py-16">
        <form
          className="card space-y-4 p-6"
          onSubmit={(e) => {
            e.preventDefault()
            try {
              sessionStorage.setItem(TOKEN_KEY, input)
            } catch {
              /* ignore */
            }
            setToken(input)
          }}
        >
          <h1 className="text-lg font-semibold">{t('admin')}</h1>
          <input
            type="password"
            className="input"
            placeholder={t('adminToken')}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            autoFocus
          />
          <button className="btn-primary w-full" disabled={!input}>
            {t('login')}
          </button>
        </form>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-6xl px-4 py-6">
      <div className="mb-4 flex items-center gap-2">
        {(['keys', 'models'] as const).map((k) => (
          <button
            key={k}
            onClick={() => setTab(k)}
            className={`btn border ${
              tab === k
                ? 'border-blue-600 bg-blue-50 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300'
                : 'border-slate-300 dark:border-slate-700'
            }`}
          >
            {k === 'keys' ? t('apiKeys') : t('models')}
          </button>
        ))}
        <button className="btn-ghost ml-auto" onClick={logout}>
          <LogOut size={16} /> {t('logout')}
        </button>
      </div>

      {tab === 'keys' ? <KeysPanel token={token} onUnauthorized={logout} /> : <ModelsPanel token={token} onUnauthorized={logout} />}
    </div>
  )
}

interface PanelProps {
  token: string
  onUnauthorized: () => void
}

function useAdminCall(onUnauthorized: () => void) {
  const [error, setError] = useState('')
  const call = useCallback(
    async <T,>(fn: () => Promise<T>): Promise<T | undefined> => {
      setError('')
      try {
        return await fn()
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) onUnauthorized()
        setError(e instanceof Error ? e.message : String(e))
        return undefined
      }
    },
    [onUnauthorized],
  )
  return { error, call }
}

function KeysPanel({ token, onUnauthorized }: PanelProps) {
  const { t, lang } = useI18n()
  const { error, call } = useAdminCall(onUnauthorized)
  const [keys, setKeys] = useState<ApiKeyDto[] | null>(null)
  const [name, setName] = useState('')
  const [reqLimit, setReqLimit] = useState(60)
  const [charLimit, setCharLimit] = useState('')

  const load = useCallback(async () => {
    const res = await call(() => adminApi.listKeys(token))
    if (res) setKeys(res)
  }, [call, token])

  useEffect(() => {
    void load()
  }, [load])

  const create = async () => {
    const res = await call(() =>
      adminApi.createKey(token, { name, reqLimit, charLimit: charLimit === '' ? null : Number(charLimit) }),
    )
    if (res) {
      setName('')
      setCharLimit('')
      void load()
    }
  }

  const fmt = (d: string | null) => (d ? new Date(d).toLocaleString(lang) : t('never'))

  return (
    <div className="space-y-4">
      <div className="card grid gap-3 p-4 sm:grid-cols-[2fr_1fr_1fr_auto] sm:items-end">
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">{t('name')}</span>
          <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="My app" />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">{t('reqLimit')} (0 = ∞)</span>
          <input className="input" type="number" min={0} value={reqLimit} onChange={(e) => setReqLimit(Number(e.target.value))} />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">{t('charLimit')}</span>
          <input
            className="input"
            type="number"
            min={-1}
            value={charLimit}
            placeholder={t('serverDefault')}
            onChange={(e) => setCharLimit(e.target.value)}
          />
        </label>
        <button className="btn-primary" onClick={create}>
          <Plus size={16} /> {t('create')}
        </button>
      </div>

      {error && <div className="text-sm text-red-600">{error}</div>}

      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-slate-200 text-xs uppercase text-slate-500 dark:border-slate-800">
            <tr>
              <th className="px-4 py-3">{t('name')}</th>
              <th className="px-4 py-3">{t('apiKey')}</th>
              <th className="px-4 py-3">{t('reqLimit')}</th>
              <th className="px-4 py-3">{t('charLimit')}</th>
              <th className="px-4 py-3">{t('status')}</th>
              <th className="px-4 py-3">{t('lastUsed')}</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody>
            {keys === null && (
              <tr>
                <td colSpan={7} className="px-4 py-6 text-center">
                  <Loader2 className="mx-auto animate-spin" size={18} />
                </td>
              </tr>
            )}
            {keys?.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-6 text-center text-slate-500">
                  {t('empty')}
                </td>
              </tr>
            )}
            {keys?.map((k) => (
              <tr key={k.id} className="border-b border-slate-100 last:border-0 dark:border-slate-800/60">
                <td className="px-4 py-3 font-medium">{k.name}</td>
                <td className="px-4 py-3">
                  <button
                    className="inline-flex items-center gap-1 font-mono text-xs text-slate-600 hover:text-blue-600 dark:text-slate-300"
                    onClick={() => navigator.clipboard.writeText(k.key)}
                    title={t('copy')}
                  >
                    {k.key.slice(0, 8)}…{k.key.slice(-4)} <Copy size={12} />
                  </button>
                </td>
                <td className="px-4 py-3">{k.reqLimit === 0 ? t('unlimited') : k.reqLimit}</td>
                <td className="px-4 py-3">
                  {k.charLimit === null ? t('serverDefault') : k.charLimit < 0 ? t('unlimited') : k.charLimit}
                </td>
                <td className="px-4 py-3">
                  <span
                    className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                      k.isActive
                        ? 'bg-green-100 text-green-700 dark:bg-green-950 dark:text-green-400'
                        : 'bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-400'
                    }`}
                  >
                    {k.isActive ? t('active') : t('disabled')}
                  </span>
                </td>
                <td className="px-4 py-3 text-xs text-slate-500">{fmt(k.lastUsedAt)}</td>
                <td className="px-4 py-3 text-right">
                  <div className="flex justify-end gap-1">
                    <button
                      className="btn-ghost px-2 py-1 text-xs"
                      onClick={async () => {
                        if (await call(() => adminApi.updateKey(token, k.id, { isActive: !k.isActive }))) void load()
                      }}
                    >
                      {k.isActive ? t('disable') : t('enable')}
                    </button>
                    <button
                      className="btn-ghost px-2 py-1 text-red-600"
                      title={t('delete')}
                      onClick={async () => {
                        if (!window.confirm(t('confirmDelete'))) return
                        await call(() => adminApi.deleteKey(token, k.id))
                        void load()
                      }}
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

const ACTIVE_STATES: ModelDownloadState[] = ['queued', 'listing', 'downloading', 'validating', 'installing']

const STATE_KEY: Record<ModelDownloadState, TKey> = {
  queued: 'dlQueued',
  listing: 'dlListing',
  downloading: 'dlDownloading',
  validating: 'dlValidating',
  installing: 'dlInstalling',
  done: 'dlDone',
  failed: 'dlFailed',
  cancelled: 'dlCancelled',
}

function formatMb(bytes: number) {
  return `${(bytes / 1048576).toFixed(bytes < 10 * 1048576 ? 1 : 0)} MB`
}

function DownloadCard({ job, onCancel }: { job: ModelDownloadStatus; onCancel: () => void }) {
  const { t } = useI18n()
  const active = ACTIVE_STATES.includes(job.state)
  const pct = job.totalBytes > 0 ? Math.min(100, (job.downloadedBytes / job.totalBytes) * 100) : 0
  const color =
    job.state === 'failed' ? 'text-red-600' : job.state === 'done' ? 'text-green-600' : active ? 'text-blue-600' : 'text-slate-500'

  return (
    <div className="card space-y-2 p-4 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        {active && <Loader2 size={14} className="animate-spin text-blue-600" />}
        <span className="font-mono font-medium">{job.pair}</span>
        <span className="break-all font-mono text-xs text-slate-500">{job.repo}</span>
        {job.quantized && <span className="rounded bg-slate-200 px-1.5 text-xs dark:bg-slate-800">int8</span>}
        <span className={`ml-auto font-medium ${color}`}>{t(STATE_KEY[job.state])}</span>
        {active && job.state !== 'installing' && (
          <button className="btn-ghost px-2 py-1 text-xs" onClick={onCancel}>
            <X size={14} /> {t('cancel')}
          </button>
        )}
      </div>
      {job.totalBytes > 0 && (
        <>
          <div className="h-2 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
            <div
              className={`h-full transition-all ${
                job.state === 'failed'
                  ? 'bg-red-500'
                  : job.state === 'cancelled'
                    ? 'bg-slate-400'
                    : job.state === 'done'
                      ? 'bg-green-600'
                      : 'bg-blue-600'
              }`}
              style={{ width: `${pct}%` }}
            />
          </div>
          <div className="text-xs text-slate-500">
            {formatMb(job.downloadedBytes)} / {formatMb(job.totalBytes)} ({pct.toFixed(0)}%)
          </div>
        </>
      )}
      {active && job.files.length > 0 && (
        <ul className="grid gap-x-4 gap-y-0.5 font-mono text-xs text-slate-500 sm:grid-cols-2">
          {job.files.map((f) => (
            <li key={f.name} className="flex justify-between gap-2">
              <span className="truncate">{f.name}</span>
              <span className={f.state === 'done' ? 'text-green-600' : f.state === 'failed' ? 'text-red-600' : ''}>
                {f.state === 'done' ? '✓' : f.size > 0 ? `${Math.floor((f.downloaded / f.size) * 100)}%` : f.state}
              </span>
            </li>
          ))}
        </ul>
      )}
      {job.error && <div className="break-words text-red-600">{job.error}</div>}
      {job.message && job.state === 'done' && <div className="break-words text-slate-600 dark:text-slate-300">{job.message}</div>}
    </div>
  )
}

function AddModelForm({ disabled, onStart }: { disabled: boolean; onStart: (r: ModelDownloadRequest) => Promise<void> }) {
  const { t } = useI18n()
  const [pair, setPair] = useState('')
  const [repo, setRepo] = useState('')
  const [prefix, setPrefix] = useState('')
  const [quantized, setQuantized] = useState(false)
  const [overwrite, setOverwrite] = useState(false)
  const pairOk = /^[a-z]{2,3}-[a-z]{2,3}$/.test(pair.trim().toLowerCase())

  return (
    <form
      className="card space-y-3 p-4"
      onSubmit={async (e) => {
        e.preventDefault()
        await onStart({
          pair: pair.trim().toLowerCase(),
          repo: repo.trim() || undefined,
          targetPrefix: prefix.trim() || undefined,
          quantized,
          overwrite,
        })
      }}
    >
      <div>
        <h2 className="font-semibold">{t('addModel')}</h2>
        <p className="text-xs text-slate-500">{t('addModelHint')}</p>
      </div>
      <div className="grid gap-3 sm:grid-cols-[1fr_2fr_1fr]">
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">{t('pair')}</span>
          <input className="input font-mono" value={pair} onChange={(e) => setPair(e.target.value)} placeholder="ja-en" />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">
            {t('hfRepo')} ({t('optional')})
          </span>
          <input
            className="input font-mono"
            value={repo}
            onChange={(e) => setRepo(e.target.value)}
            placeholder={`Xenova/opus-mt-${pairOk ? pair.trim().toLowerCase() : 'ja-en'}`}
          />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-slate-500">
            {t('targetPrefix')} ({t('optional')})
          </span>
          <input className="input font-mono" value={prefix} onChange={(e) => setPrefix(e.target.value)} placeholder=">>vie<<" />
        </label>
      </div>
      <div className="flex flex-wrap items-center gap-4 text-sm">
        <label className="inline-flex items-center gap-2">
          <input type="checkbox" checked={quantized} onChange={(e) => setQuantized(e.target.checked)} /> {t('quantized')}
        </label>
        <label className="inline-flex items-center gap-2">
          <input type="checkbox" checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} /> {t('overwrite')}
        </label>
        <button className="btn-primary ml-auto" disabled={disabled || !pairOk}>
          <Download size={16} /> {t('downloadModel')}
        </button>
      </div>
    </form>
  )
}

function ModelsPanel({ token, onUnauthorized }: PanelProps) {
  const { t, languageName } = useI18n()
  const { error, call } = useAdminCall(onUnauthorized)
  const [models, setModels] = useState<ModelInfo[] | null>(null)
  const [downloads, setDownloads] = useState<ModelDownloadStatus[]>([])
  const [busy, setBusy] = useState(false)
  const anyActive = downloads.some((d) => ACTIVE_STATES.includes(d.state))
  const wasActive = useRef(false)

  const loadModels = useCallback(async () => {
    const res = await call(() => adminApi.listModels(token))
    if (res) setModels(res)
  }, [call, token])

  const loadDownloads = useCallback(async () => {
    try {
      setDownloads(await adminApi.listDownloads(token))
    } catch {
      /* lỗi tạm thời khi hỏi tiến trình: bỏ qua, lần sau hỏi lại */
    }
  }, [token])

  useEffect(() => {
    void loadModels()
    void loadDownloads()
  }, [loadModels, loadDownloads])

  // Hỏi tiến trình mỗi giây khi đang có lượt tải; xong thì nạp lại danh sách model
  useEffect(() => {
    if (!anyActive) {
      if (wasActive.current) void loadModels()
      wasActive.current = false
      return
    }
    wasActive.current = true
    const timer = window.setInterval(() => void loadDownloads(), 1000)
    return () => window.clearInterval(timer)
  }, [anyActive, loadDownloads, loadModels])

  const reload = async () => {
    setBusy(true)
    const res = await call(() => adminApi.reloadModels(token))
    if (res) setModels(res)
    setBusy(false)
  }

  const start = async (req: ModelDownloadRequest) => {
    const res = await call(() => adminApi.startDownload(token, req))
    if (res) setDownloads((d) => [res, ...d.filter((x) => x.id !== res.id)])
  }

  return (
    <div className="space-y-4">
      <AddModelForm disabled={anyActive} onStart={start} />

      {downloads.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-xs font-medium uppercase text-slate-500">{t('recentDownloads')}</h3>
          {downloads.slice(0, 3).map((job) => (
            <DownloadCard
              key={job.id}
              job={job}
              onCancel={async () => {
                await call(() => adminApi.cancelDownload(token, job.id))
                void loadDownloads()
              }}
            />
          ))}
        </div>
      )}

      <div className="flex items-center gap-3">
        <button className="btn-primary" onClick={reload} disabled={busy}>
          <RefreshCw size={16} className={busy ? 'animate-spin' : ''} /> {t('reload')}
        </button>
        {error && <span className="text-sm text-red-600">{error}</span>}
      </div>
      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-slate-200 text-xs uppercase text-slate-500 dark:border-slate-800">
            <tr>
              <th className="px-4 py-3">{t('pair')}</th>
              <th className="px-4 py-3">{t('folder')}</th>
              <th className="px-4 py-3">{t('status')}</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody>
            {models?.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-6 text-center text-slate-500">
                  {t('noModels')}
                </td>
              </tr>
            )}
            {models?.map((m) => (
              <tr key={`${m.source}-${m.target}`} className="border-b border-slate-100 last:border-0 dark:border-slate-800/60">
                <td className="px-4 py-3 font-medium">
                  {languageName(m.source)} → {languageName(m.target)}
                  <span className="ml-2 font-mono text-xs text-slate-500">
                    {m.source}-{m.target}
                  </span>
                </td>
                <td className="px-4 py-3 font-mono text-xs">{m.name}</td>
                <td className="px-4 py-3">
                  {m.error ? (
                    <span className="text-red-600" title={m.error}>
                      {t('error')}: {m.error}
                    </span>
                  ) : m.loaded ? (
                    <span className="text-green-600">{t('loaded')}</span>
                  ) : (
                    <span className="text-slate-500">{t('notLoaded')}</span>
                  )}
                </td>
                <td className="px-4 py-3 text-right">
                  <button
                    className="btn-ghost px-2 py-1 text-red-600"
                    title={t('delete')}
                    disabled={anyActive}
                    onClick={async () => {
                      if (!window.confirm(`${t('confirmDeleteModel')}\n\n${m.name}`)) return
                      const res = await call(() => adminApi.deleteModel(token, m.name))
                      if (res) setModels(res)
                    }}
                  >
                    <Trash2 size={14} />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
