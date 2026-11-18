import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ArrowLeftRight, Check, Copy, Download, FileText, Loader2, Type, Upload, X } from 'lucide-react'
import { api, ApiError, type DetectedLanguage, type FrontendSettings, type Language } from '../api/client'
import { LanguageSelect } from '../components/LanguageSelect'
import { useI18n } from '../lib/i18n'

interface Props {
  settings: FrontendSettings
  languages: Language[]
}

type Mode = 'text' | 'files'

export function TranslatePage({ settings, languages }: Props) {
  const { t } = useI18n()
  const [mode, setMode] = useState<Mode>('text')

  const allCodes = useMemo(() => languages.map((l) => l.code), [languages])
  const [source, setSource] = useState(() =>
    settings.language.source.code === 'auto' || allCodes.includes(settings.language.source.code)
      ? settings.language.source.code
      : 'auto',
  )

  const targetCodes = useMemo(() => {
    if (source === 'auto') return Array.from(new Set(languages.flatMap((l) => l.targets)))
    return languages.find((l) => l.code === source)?.targets ?? []
  }, [languages, source])

  const [target, setTarget] = useState(settings.language.target.code)

  // Đích không hợp lệ với nguồn hiện tại → chọn đích đầu tiên khả dụng
  useEffect(() => {
    if (targetCodes.length > 0 && !targetCodes.includes(target)) {
      setTarget(targetCodes.includes(settings.language.target.code) ? settings.language.target.code : targetCodes[0])
    }
  }, [targetCodes, target, settings.language.target.code])

  return (
    <div className="mx-auto max-w-6xl px-4 py-6">
      <div className="mb-4 flex gap-2">
        <TabButton active={mode === 'text'} onClick={() => setMode('text')} icon={<Type size={16} />}>
          {t('text')}
        </TabButton>
        {settings.filesTranslation && (
          <TabButton active={mode === 'files'} onClick={() => setMode('files')} icon={<FileText size={16} />}>
            {t('files')}
          </TabButton>
        )}
      </div>

      {mode === 'text' ? (
        <TextTranslator
          settings={settings}
          languages={languages}
          allCodes={allCodes}
          targetCodes={targetCodes}
          source={source}
          target={target}
          setSource={setSource}
          setTarget={setTarget}
        />
      ) : (
        <FileTranslator
          settings={settings}
          languages={languages}
          allCodes={allCodes}
          targetCodes={targetCodes}
          source={source}
          target={target}
          setSource={setSource}
          setTarget={setTarget}
        />
      )}
    </div>
  )
}

function TabButton({
  active,
  onClick,
  icon,
  children,
}: {
  active: boolean
  onClick: () => void
  icon: ReactNode
  children: ReactNode
}) {
  return (
    <button
      onClick={onClick}
      className={`btn border ${
        active
          ? 'border-blue-600 bg-blue-50 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300'
          : 'border-slate-300 text-slate-600 hover:bg-slate-100 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800'
      }`}
    >
      {icon}
      {children}
    </button>
  )
}

interface PanelProps {
  settings: FrontendSettings
  languages: Language[]
  allCodes: string[]
  targetCodes: string[]
  source: string
  target: string
  setSource: (s: string) => void
  setTarget: (s: string) => void
}

function LanguageBar({
  languages,
  allCodes,
  targetCodes,
  source,
  target,
  setSource,
  setTarget,
  detected,
  onSwap,
}: Omit<PanelProps, 'settings'> & { detected?: string; onSwap: () => void }) {
  const { t } = useI18n()
  const canSwap = source !== 'auto' || !!detected
  return (
    <div className="grid grid-cols-[1fr_auto_1fr] items-center gap-2 border-b border-slate-200 px-2 py-1 dark:border-slate-800">
      <LanguageSelect
        label="source"
        value={source}
        onChange={setSource}
        codes={allCodes}
        languages={languages}
        includeAuto
        detected={detected}
      />
      <button className="btn-ghost rounded-full p-2" onClick={onSwap} disabled={!canSwap} title={t('swap')}>
        <ArrowLeftRight size={18} />
      </button>
      <LanguageSelect label="target" value={target} onChange={setTarget} codes={targetCodes} languages={languages} />
    </div>
  )
}

// ------------------------------------------------------------------------------------ Text

function TextTranslator(props: PanelProps) {
  const { settings, source, target, setSource, setTarget } = props
  const { t } = useI18n()
  const [text, setText] = useState('')
  const [result, setResult] = useState('')
  const [detected, setDetected] = useState<DetectedLanguage | undefined>()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)

  const overLimit = settings.charLimit >= 0 && text.length > settings.charLimit

  // Dịch tự động sau khi ngừng gõ 500ms
  useEffect(() => {
    if (!text.trim()) {
      setResult('')
      setDetected(undefined)
      setError('')
      return
    }
    if (overLimit || !target) return

    const controller = new AbortController()
    const timer = setTimeout(async () => {
      setLoading(true)
      setError('')
      try {
        const res = await api.translate(text, source, target, 'text', controller.signal)
        setResult(res.translatedText)
        setDetected(res.detectedLanguage)
      } catch (e) {
        if (controller.signal.aborted) return
        setError(e instanceof ApiError ? e.message : t('loadError'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }, 500)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [text, source, target, overLimit, t])

  const swap = () => {
    const newSource = source === 'auto' ? detected?.language : source
    if (!newSource) return
    setSource(target)
    setTarget(newSource)
    if (result) setText(result)
  }

  const copy = async () => {
    await navigator.clipboard.writeText(result)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <div className="card overflow-hidden">
      <LanguageBar {...props} detected={source === 'auto' ? detected?.language : undefined} onSwap={swap} />

      <div className="grid md:grid-cols-2">
        {/* Nguồn */}
        <div className="relative border-b border-slate-200 md:border-b-0 md:border-r dark:border-slate-800">
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('sourcePlaceholder')}
            className="block h-64 w-full resize-none bg-transparent p-4 pr-10 text-lg outline-none md:h-80"
            autoFocus
          />
          {text && (
            <button className="btn-ghost absolute right-2 top-2 p-1.5" onClick={() => setText('')} title={t('clear')}>
              <X size={18} />
            </button>
          )}
          <div className="flex items-center justify-between px-4 pb-3 text-xs text-slate-500">
            <span>
              {source === 'auto' && detected && detected.confidence > 0 && (
                <>
                  {t('detected')}: <b>{detected.language}</b> ({Math.round(detected.confidence)}%)
                </>
              )}
            </span>
            <span className={overLimit ? 'font-semibold text-red-600' : ''}>
              {text.length}
              {settings.charLimit >= 0 && ` / ${settings.charLimit}`} {t('chars')}
            </span>
          </div>
        </div>

        {/* Đích */}
        <div className="relative bg-slate-50/60 dark:bg-slate-900/40">
          <div className="h-64 overflow-auto whitespace-pre-wrap p-4 text-lg md:h-80">
            {error ? (
              <span className="text-red-600">{error}</span>
            ) : result ? (
              result
            ) : (
              <span className="text-slate-400">{loading ? t('translating') : t('translationPlaceholder')}</span>
            )}
          </div>
          <div className="flex items-center justify-between px-4 pb-3 text-xs text-slate-500">
            <span className="flex items-center gap-1">{loading && <Loader2 size={14} className="animate-spin" />}</span>
            <button className="btn-ghost px-2 py-1" onClick={copy} disabled={!result}>
              {copied ? <Check size={14} /> : <Copy size={14} />}
              {copied ? t('copied') : t('copy')}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

// ------------------------------------------------------------------------------------ Files

function FileTranslator(props: PanelProps) {
  const { settings, source, target } = props
  const { t } = useI18n()
  const [file, setFile] = useState<File | null>(null)
  const [downloadUrl, setDownloadUrl] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [dragging, setDragging] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const accept = settings.supportedFilesFormat.join(',')

  const pick = (f: File | undefined | null) => {
    if (!f) return
    setFile(f)
    setDownloadUrl('')
    setError('')
  }

  const run = async () => {
    if (!file) return
    setLoading(true)
    setError('')
    setDownloadUrl('')
    try {
      const res = await api.translateFile(file, source, target)
      setDownloadUrl(res.translatedFileUrl)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('loadError'))
    } finally {
      setLoading(false)
    }
  }

  const swap = () => {
    if (source === 'auto') return
    props.setSource(target)
    props.setTarget(source)
  }

  return (
    <div className="card overflow-hidden">
      <LanguageBar {...props} onSwap={swap} />

      <div className="p-6">
        <div
          onClick={() => inputRef.current?.click()}
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => {
            e.preventDefault()
            setDragging(false)
            pick(e.dataTransfer.files?.[0])
          }}
          className={`flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed p-10 text-center transition ${
            dragging
              ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40'
              : 'border-slate-300 hover:border-blue-400 dark:border-slate-700'
          }`}
        >
          <Upload className="text-slate-400" size={32} />
          {file ? (
            <span className="font-medium">
              {file.name} <span className="text-slate-500">({(file.size / 1024).toFixed(1)} KB)</span>
            </span>
          ) : (
            <span className="font-medium">{t('chooseFile')}</span>
          )}
          <span className="text-xs text-slate-500">
            {t('supported')}: {settings.supportedFilesFormat.join(', ')} · {t('maxSize')} {settings.fileSizeLimitMb} MB
          </span>
          <input
            ref={inputRef}
            type="file"
            accept={accept}
            className="hidden"
            onChange={(e) => pick(e.target.files?.[0])}
          />
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-3">
          <button className="btn-primary" onClick={run} disabled={!file || loading || !target}>
            {loading ? <Loader2 size={16} className="animate-spin" /> : <FileText size={16} />}
            {loading ? t('translating') : t('translateFile')}
          </button>
          {downloadUrl && (
            <a className="btn border border-green-600 text-green-700 hover:bg-green-50 dark:text-green-400 dark:hover:bg-green-950/40" href={downloadUrl}>
              <Download size={16} />
              {t('download')}
            </a>
          )}
          {error && <span className="text-sm text-red-600">{error}</span>}
        </div>
      </div>
    </div>
  )
}
