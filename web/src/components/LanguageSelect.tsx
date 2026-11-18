import { useMemo } from 'react'
import { useI18n } from '../lib/i18n'
import type { Language } from '../api/client'

interface Props {
  value: string
  onChange: (code: string) => void
  /** Danh sách mã ngôn ngữ được phép chọn */
  codes: string[]
  languages: Language[]
  includeAuto?: boolean
  detected?: string
  label: string
}

export function LanguageSelect({ value, onChange, codes, languages, includeAuto, detected, label }: Props) {
  const { t, languageName } = useI18n()

  const options = useMemo(() => {
    const byCode = new Map(languages.map((l) => [l.code, l.name]))
    return codes
      .map((code) => ({ code, name: languageName(code, byCode.get(code)) }))
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [codes, languages, languageName])

  const autoLabel = detected ? `${t('autoDetect')} (${languageName(detected)})` : t('autoDetect')

  return (
    <select
      aria-label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="w-full cursor-pointer appearance-none rounded-lg border-0 bg-transparent px-3 py-2 text-sm font-semibold text-blue-700 outline-none hover:bg-slate-100 focus:ring-2 focus:ring-blue-500/30 dark:text-blue-400 dark:hover:bg-slate-800"
    >
      {includeAuto && <option value="auto">{autoLabel}</option>}
      {options.map((o) => (
        <option key={o.code} value={o.code}>
          {o.name}
        </option>
      ))}
    </select>
  )
}
