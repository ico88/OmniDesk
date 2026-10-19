let csrf = '';
export async function refreshCsrf(): Promise<void> {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('Impossibile inizializzare la sessione');
  csrf = (await response.json()).token;
}
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (method !== 'GET' && !csrf) await refreshCsrf();
  const response = await fetch(`/api${path}`, {
    method, credentials: 'same-origin', cache: 'no-store',
    headers: { 'Content-Type': 'application/json', ...(method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    if (response.status === 409) {
      const detail = await response.json().catch(() => null) as { error?: unknown } | null;
      if (typeof detail?.error === 'string' && detail.error.length <= 300) throw new Error(detail.error);
    }
    throw new Error(response.status === 401 ? 'Sessione scaduta o credenziali non valide' : response.status === 403 ? 'Permesso non concesso' : `Operazione non riuscita (${response.status})`);
  }
  return response.status === 204 ? undefined as T : response.json();
}
