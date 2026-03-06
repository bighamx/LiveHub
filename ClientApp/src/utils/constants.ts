export const WORKER_PROXY_BASE = 'https://proxy.xbyham.com/';

export const API_TARGET_BASE =
  (import.meta.env.VITE_API_BASE as string | undefined)?.trim() || window.location.origin;

export const EXTERNAL_API_BASE = 'http://api.vipmisss.com:81/mf';

export function buildApiUrl(path: string, useProxy = true): string {
  if (!useProxy) return path;

  const pathParts = path.split('/').filter(Boolean);
  const lastSegment = pathParts.length > 0 ? pathParts[pathParts.length - 1] : '';
  const absoluteApiUrl =
    path === '/api/channels'
      ? `${EXTERNAL_API_BASE}/json.txt`
      : `${EXTERNAL_API_BASE}/${lastSegment}`;
  return `${WORKER_PROXY_BASE}${absoluteApiUrl}`;
}

export function buildWorkerProxyUrl(targetUrl: string): string {
  return `${WORKER_PROXY_BASE}${targetUrl}`;
}

export function buildBackendM3u8ProxyUrl(targetUrl: string): string {
  return `/api/stream/proxym3u8?url=${encodeURIComponent(targetUrl)}`;
}

export function buildBackendFlvProxyUrl(targetUrl: string): string {
  return `/api/stream/proxyflv?flvUrl=${encodeURIComponent(targetUrl)}`;
}

export function safeParseJson<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key);
    if (raw === null) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

export function safeSetJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // quota exceeded or private browsing
  }
}
