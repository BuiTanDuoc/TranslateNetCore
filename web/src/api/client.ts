// Client gọi API NetTranslate (tương thích LibreTranslate)

export interface Language {
  code: string
  name: string
  targets: string[]
}

export interface DetectedLanguage {
  confidence: number
  language: string
}

export interface TranslateResponse {
  translatedText: string
  detectedLanguage?: DetectedLanguage
}

export interface FrontendSettings {
  charLimit: number
  frequencyLimit: number
  batchLimit: number
  apiKeys: boolean
  keyRequired: boolean
  suggestions: boolean
  filesTranslation: boolean
  fileSizeLimitMb: number
  supportedFilesFormat: string[]
  language: {
    source: { code: string; name: string }
    target: { code: string; name: string }
  }
}

export interface ApiKeyDto {
  id: string
  key: string
  name: string
  reqLimit: number
  charLimit: number | null
  isActive: boolean
  createdAt: string
  lastUsedAt: string | null
}

export interface ModelInfo {
  source: string
  target: string
  name: string
  loaded: boolean
  error: string | null
}

export type ModelDownloadState =
  | 'queued'
  | 'listing'
  | 'downloading'
  | 'validating'
  | 'installing'
  | 'done'
  | 'failed'
  | 'cancelled'

export interface ModelDownloadStatus {
  id: string
  pair: string
  repo: string
  quantized: boolean
  state: ModelDownloadState
  message: string | null
  error: string | null
  totalBytes: number
  downloadedBytes: number
  startedAt: string
  finishedAt: string | null
  files: { name: string; size: number; downloaded: number; state: string }[]
}

export interface ModelRepoCandidate {
  repo: string
  fullBytes: number | null
  quantizedBytes: number | null
  downloads: number | null
  recommended: boolean
  note: string | null
  warning: string | null
  targetPrefix: string | null
}

export interface ModelDownloadRequest {
  pair: string
  repo?: string
  quantized?: boolean
  overwrite?: boolean
  targetPrefix?: string
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

const API_KEY_STORAGE = 'nt-api-key'

export function getStoredApiKey(): string {
  try {
    return localStorage.getItem(API_KEY_STORAGE) ?? ''
  } catch {
    return ''
  }
}

export function setStoredApiKey(key: string) {
  try {
    if (key) localStorage.setItem(API_KEY_STORAGE, key)
    else localStorage.removeItem(API_KEY_STORAGE)
  } catch {
    /* ignore */
  }
}

async function request<T>(url: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  // Header để server nhận biết request từ Web UI (miễn API key nếu được cấu hình)
  headers.set('X-NetTranslate-Client', 'web')

  const res = await fetch(url, { ...init, headers, signal: init.signal })
  const text = await res.text()
  let body: unknown = null
  try {
    body = text ? JSON.parse(text) : null
  } catch {
    body = null
  }

  if (!res.ok) {
    const message =
      body && typeof body === 'object' && 'error' in body ? String((body as { error: unknown }).error) : `HTTP ${res.status}`
    throw new ApiError(res.status, message)
  }
  return body as T
}

function withKey<T extends object>(payload: T): T & { api_key?: string } {
  const key = getStoredApiKey()
  return key ? { ...payload, api_key: key } : payload
}

export const api = {
  settings: () => request<FrontendSettings>('/frontend/settings'),

  languages: () => request<Language[]>('/languages'),

  translate: (q: string, source: string, target: string, format: 'text' | 'html' = 'text', signal?: AbortSignal) =>
    request<TranslateResponse>('/translate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(withKey({ q, source, target, format })),
      signal,
    }),

  detect: (q: string) =>
    request<DetectedLanguage[]>('/detect', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(withKey({ q })),
    }),

  translateFile: (file: File, source: string, target: string) => {
    const form = new FormData()
    form.append('file', file)
    form.append('source', source)
    form.append('target', target)
    const key = getStoredApiKey()
    if (key) form.append('api_key', key)
    return request<{ translatedFileUrl: string }>('/translate_file', { method: 'POST', body: form })
  },
}

export const adminApi = {
  headers: (token: string, json = false): HeadersInit => ({
    'X-Admin-Token': token,
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  }),

  listKeys: (token: string) => request<ApiKeyDto[]>('/admin/keys', { headers: adminApi.headers(token) }),

  createKey: (token: string, data: { name: string; reqLimit: number; charLimit: number | null }) =>
    request<ApiKeyDto>('/admin/keys', {
      method: 'POST',
      headers: adminApi.headers(token, true),
      body: JSON.stringify(data),
    }),

  updateKey: (token: string, id: string, data: { isActive?: boolean; reqLimit?: number; name?: string }) =>
    request<ApiKeyDto>(`/admin/keys/${id}`, {
      method: 'PATCH',
      headers: adminApi.headers(token, true),
      body: JSON.stringify(data),
    }),

  deleteKey: (token: string, id: string) =>
    request<null>(`/admin/keys/${id}`, { method: 'DELETE', headers: adminApi.headers(token) }),

  listModels: (token: string) => request<ModelInfo[]>('/admin/models', { headers: adminApi.headers(token) }),

  reloadModels: (token: string) =>
    request<ModelInfo[]>('/admin/models/reload', { method: 'POST', headers: adminApi.headers(token) }),

  deleteModel: (token: string, name: string) =>
    request<ModelInfo[]>(`/admin/models/${encodeURIComponent(name)}`, { method: 'DELETE', headers: adminApi.headers(token) }),

  startDownload: (token: string, data: ModelDownloadRequest) =>
    request<ModelDownloadStatus>('/admin/models/download', {
      method: 'POST',
      headers: adminApi.headers(token, true),
      body: JSON.stringify(data),
    }),

  searchRepos: (token: string, pair: string, signal?: AbortSignal) =>
    request<ModelRepoCandidate[]>(`/admin/models/repos?pair=${encodeURIComponent(pair)}`, {
      headers: adminApi.headers(token),
      signal,
    }),

  listDownloads: (token: string) =>
    request<ModelDownloadStatus[]>('/admin/models/downloads', { headers: adminApi.headers(token) }),

  cancelDownload: (token: string, id: string) =>
    request<ModelDownloadStatus>(`/admin/models/downloads/${id}/cancel`, { method: 'POST', headers: adminApi.headers(token) }),
}
