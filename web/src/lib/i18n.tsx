import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export type UiLang = 'vi' | 'en'

const dict = {
  vi: {
    appTagline: 'API dịch máy mã nguồn mở, chạy offline',
    text: 'Văn bản',
    files: 'Tệp',
    admin: 'Quản trị',
    apiDocs: 'Tài liệu API',
    autoDetect: 'Tự động phát hiện',
    detected: 'Đã phát hiện',
    swap: 'Đảo ngôn ngữ',
    sourcePlaceholder: 'Nhập văn bản cần dịch…',
    translationPlaceholder: 'Bản dịch',
    translating: 'Đang dịch…',
    copy: 'Sao chép',
    copied: 'Đã chép!',
    clear: 'Xoá',
    chars: 'ký tự',
    noModels: 'Chưa có model dịch nào. Hãy tải model vào thư mục models/ rồi khởi động lại server (xem README).',
    loadError: 'Không kết nối được tới server',
    chooseFile: 'Chọn hoặc kéo thả tệp vào đây',
    supported: 'Hỗ trợ',
    maxSize: 'tối đa',
    translateFile: 'Dịch tệp',
    download: 'Tải bản dịch',
    apiKey: 'API key',
    apiKeyHint: 'Không bắt buộc. Dùng key riêng để có giới hạn cao hơn.',
    save: 'Lưu',
    theme: 'Giao diện',
    language: 'Ngôn ngữ',
    adminToken: 'Admin token',
    login: 'Đăng nhập',
    logout: 'Đăng xuất',
    apiKeys: 'API keys',
    models: 'Model',
    name: 'Tên',
    reqLimit: 'Request/phút',
    charLimit: 'Ký tự/request',
    status: 'Trạng thái',
    created: 'Tạo lúc',
    lastUsed: 'Dùng lần cuối',
    active: 'Hoạt động',
    disabled: 'Đã khoá',
    create: 'Tạo key',
    delete: 'Xoá',
    enable: 'Mở khoá',
    disable: 'Khoá',
    confirmDelete: 'Xoá API key này?',
    unlimited: 'Không giới hạn',
    serverDefault: 'Mặc định server',
    reload: 'Quét lại',
    loaded: 'Đã nạp',
    notLoaded: 'Chưa nạp',
    error: 'Lỗi',
    pair: 'Cặp ngôn ngữ',
    folder: 'Thư mục',
    empty: 'Chưa có dữ liệu',
    never: 'Chưa dùng',
    addModel: 'Thêm model từ Hugging Face',
    addModelHint: 'Nhập cặp ngôn ngữ, chọn repo trong danh sách (★ = khuyên dùng). Server tải file ONNX, kiểm tra từng file, cài rồi dịch thử.',
    hfRepo: 'Repo Hugging Face',
    targetPrefix: 'Token ngôn ngữ đích',
    optional: 'tuỳ chọn',
    quantized: 'Bản quantized (int8, nhỏ hơn)',
    overwrite: 'Ghi đè nếu đã có',
    downloadModel: 'Tải về',
    cancel: 'Huỷ',
    confirmDeleteModel: 'Xoá thư mục model này khỏi server?',
    dlQueued: 'Đang chờ',
    dlListing: 'Đang đọc danh sách file',
    dlDownloading: 'Đang tải',
    dlValidating: 'Đang kiểm tra file',
    dlInstalling: 'Đang cài và dịch thử',
    dlDone: 'Hoàn tất',
    dlFailed: 'Thất bại',
    dlCancelled: 'Đã huỷ',
    recentDownloads: 'Lượt tải gần đây',
    enterPairFirst: 'Nhập cặp ngôn ngữ để tìm repo',
    searchingRepos: 'Đang tìm repo phù hợp trên Hugging Face…',
    noReposFound: 'Không tìm thấy repo ONNX nào dùng được cho cặp này. Có thể nhập tay repo khác.',
    customRepo: 'Khác (nhập tay)…',
    recommended: 'Khuyên dùng',
    downloadsShort: 'lượt tải',
    advanced: 'Nâng cao',
    targetPrefixHint:
      'Chỉ dùng cho model dịch ra nhiều ngôn ngữ (vd. opus-mt-en-mul): chữ đặt đầu câu để chọn ngôn ngữ đích, như >>vie<< hay >>jpn<<. Model một cặp thì để trống.',
  },
  en: {
    appTagline: 'Free and open source machine translation API, runs offline',
    text: 'Text',
    files: 'Files',
    admin: 'Admin',
    apiDocs: 'API Docs',
    autoDetect: 'Auto detect',
    detected: 'Detected',
    swap: 'Swap languages',
    sourcePlaceholder: 'Enter text to translate…',
    translationPlaceholder: 'Translation',
    translating: 'Translating…',
    copy: 'Copy',
    copied: 'Copied!',
    clear: 'Clear',
    chars: 'chars',
    noModels: 'No translation models installed. Put models into the models/ folder and restart the server (see README).',
    loadError: 'Cannot connect to the server',
    chooseFile: 'Choose or drop a file here',
    supported: 'Supported',
    maxSize: 'max',
    translateFile: 'Translate file',
    download: 'Download translation',
    apiKey: 'API key',
    apiKeyHint: 'Optional. Use your own key for higher limits.',
    save: 'Save',
    theme: 'Theme',
    language: 'Language',
    adminToken: 'Admin token',
    login: 'Sign in',
    logout: 'Sign out',
    apiKeys: 'API keys',
    models: 'Models',
    name: 'Name',
    reqLimit: 'Requests/min',
    charLimit: 'Chars/request',
    status: 'Status',
    created: 'Created',
    lastUsed: 'Last used',
    active: 'Active',
    disabled: 'Disabled',
    create: 'Create key',
    delete: 'Delete',
    enable: 'Enable',
    disable: 'Disable',
    confirmDelete: 'Delete this API key?',
    unlimited: 'Unlimited',
    serverDefault: 'Server default',
    reload: 'Rescan',
    loaded: 'Loaded',
    notLoaded: 'Not loaded',
    error: 'Error',
    pair: 'Pair',
    folder: 'Folder',
    empty: 'Nothing here yet',
    never: 'Never',
    addModel: 'Add model from Hugging Face',
    addModelHint: 'Enter a language pair and pick a repository from the list (★ = recommended). The server downloads the ONNX files, validates them, installs and test-translates.',
    hfRepo: 'Hugging Face repo',
    targetPrefix: 'Target language token',
    optional: 'optional',
    quantized: 'Quantized (int8, smaller)',
    overwrite: 'Overwrite if it exists',
    downloadModel: 'Download',
    cancel: 'Cancel',
    confirmDeleteModel: 'Delete this model folder from the server?',
    dlQueued: 'Queued',
    dlListing: 'Listing files',
    dlDownloading: 'Downloading',
    dlValidating: 'Validating files',
    dlInstalling: 'Installing and test-translating',
    dlDone: 'Done',
    dlFailed: 'Failed',
    dlCancelled: 'Cancelled',
    recentDownloads: 'Recent downloads',
    enterPairFirst: 'Enter a language pair to find repositories',
    searchingRepos: 'Searching Hugging Face for compatible repositories…',
    noReposFound: 'No compatible ONNX repository found for this pair. You can type another repository.',
    customRepo: 'Other (type it)…',
    recommended: 'Recommended',
    downloadsShort: 'downloads',
    advanced: 'Advanced',
    targetPrefixHint:
      'Only for models that translate into many languages (e.g. opus-mt-en-mul): a token at the start of the sentence that picks the target language, like >>vie<< or >>jpn<<. Leave empty for single-pair models.',
  },
} as const

export type TKey = keyof (typeof dict)['vi']

interface I18nValue {
  lang: UiLang
  setLang: (l: UiLang) => void
  t: (key: TKey) => string
  /** Tên ngôn ngữ theo ngôn ngữ giao diện (dùng Intl.DisplayNames) */
  languageName: (code: string, fallback?: string) => string
}

const I18nContext = createContext<I18nValue | null>(null)

function initialLang(): UiLang {
  try {
    const saved = localStorage.getItem('nt-ui-lang')
    if (saved === 'vi' || saved === 'en') return saved
  } catch {
    /* ignore */
  }
  return navigator.language?.toLowerCase().startsWith('vi') ? 'vi' : 'en'
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<UiLang>(initialLang)

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  const setLang = useCallback((l: UiLang) => {
    setLangState(l)
    try {
      localStorage.setItem('nt-ui-lang', l)
    } catch {
      /* ignore */
    }
  }, [])

  const value = useMemo<I18nValue>(() => {
    let names: Intl.DisplayNames | null = null
    try {
      names = new Intl.DisplayNames([lang], { type: 'language' })
    } catch {
      names = null
    }
    return {
      lang,
      setLang,
      t: (key) => dict[lang][key],
      languageName: (code, fallback) => {
        const mapped = code === 'zt' ? 'zh-Hant' : code === 'zh' ? 'zh-Hans' : code
        try {
          const n = names?.of(mapped)
          if (n && n !== mapped) return n.charAt(0).toLocaleUpperCase(lang) + n.slice(1)
        } catch {
          /* mã không hợp lệ */
        }
        return fallback ?? code
      },
    }
  }, [lang, setLang])

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useI18n() {
  const ctx = useContext(I18nContext)
  if (!ctx) throw new Error('useI18n must be used within I18nProvider')
  return ctx
}
