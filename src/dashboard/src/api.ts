let csrf = '';
export async function refreshCsrf(): Promise<void> {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('Impossibile inizializzare la sessione');
  csrf = (await response.json()).token;
}
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (method !== 'GET' && !csrf) await refreshCsrf();
  const multipart = typeof FormData !== 'undefined' && body instanceof FormData;
  const response = await fetch(`/api${path}`, {
    method, credentials: 'same-origin', cache: 'no-store',
    headers: { ...(multipart ? {} : { 'Content-Type': 'application/json' }), ...(method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
    body: body === undefined ? undefined : multipart ? body : JSON.stringify(body),
  });
  if (!response.ok) {
    if (response.status === 400 || response.status === 409) {
      const detail = await response.json().catch(() => null) as { error?: unknown; message?: unknown } | null;
      const message = typeof detail?.error === 'string' ? detail.error : detail?.message;
      if (typeof message === 'string' && message.length <= 300) throw new Error(message);
    }
    throw new Error(response.status === 401 ? 'Sessione scaduta o credenziali non valide' : response.status === 403 ? 'Permesso non concesso' : `Operazione non riuscita (${response.status})`);
  }
  return response.status === 204 ? undefined as T : response.json();
}
