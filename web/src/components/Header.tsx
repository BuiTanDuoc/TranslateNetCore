import { BookOpen, KeyRound, Languages, Moon, Shield, Sun } from 'lucide-react'
import { useI18n } from '../lib/i18n'
import { useTheme } from '../lib/theme'

export type Page = 'translate' | 'admin'

interface Props {
  page: Page
  onNavigate: (p: Page) => void
  showKeyButton: boolean
  onToggleKey: () => void
}

export function Header({ page, onNavigate, showKeyButton, onToggleKey }: Props) {
  const { t, lang, setLang } = useI18n()
  const { theme, toggle } = useTheme()

  return (
    <header className="sticky top-0 z-10 border-b border-slate-200 bg-white/80 backdrop-blur dark:border-slate-800 dark:bg-slate-950/80">
      <div className="mx-auto flex max-w-6xl items-center gap-3 px-4 py-3">
        <button onClick={() => onNavigate('translate')} className="flex items-center gap-2">
          <span className="grid h-9 w-9 place-items-center rounded-xl bg-blue-600 text-white">
            <Languages size={20} />
          </span>
          <span className="text-left">
            <span className="block text-base font-bold leading-tight">NetTranslate</span>
            <span className="hidden text-xs text-slate-500 sm:block dark:text-slate-400">{t('appTagline')}</span>
          </span>
        </button>

        <nav className="ml-auto flex items-center gap-1">
          <a href="/scalar/v1" target="_blank" rel="noreferrer" className="btn-ghost" title={t('apiDocs')}>
            <BookOpen size={16} />
            <span className="hidden md:inline">{t('apiDocs')}</span>
          </a>

          {showKeyButton && (
            <button className="btn-ghost" onClick={onToggleKey} title={t('apiKey')}>
              <KeyRound size={16} />
            </button>
          )}

          <button
            className={`btn-ghost ${page === 'admin' ? 'bg-slate-100 dark:bg-slate-800' : ''}`}
            onClick={() => onNavigate(page === 'admin' ? 'translate' : 'admin')}
            title={t('admin')}
          >
            <Shield size={16} />
            <span className="hidden md:inline">{t('admin')}</span>
          </button>

          <button
            className="btn-ghost w-10 font-semibold"
            onClick={() => setLang(lang === 'vi' ? 'en' : 'vi')}
            title={t('language')}
          >
            {lang === 'vi' ? 'VI' : 'EN'}
          </button>

          <button className="btn-ghost" onClick={toggle} title={t('theme')}>
            {theme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}
          </button>
        </nav>
      </div>
    </header>
  )
}
