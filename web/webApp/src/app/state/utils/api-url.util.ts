export function resolveApiBaseUrl(): string {
  if (typeof window === 'undefined') {
    return '';
  }

  const configured = (window as Window & { __UNLOAD_API_BASE_URL__?: string })
    .__UNLOAD_API_BASE_URL__;
  if (configured) {
    return configured.replace(/\/$/, '');
  }

  return '';
}

export function joinApiUrl(baseUrl: string, path: string): string {
  return `${baseUrl}${path}`;
}
