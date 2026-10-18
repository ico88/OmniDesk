import { describe, it, expect, vi, afterEach } from 'vitest';
import { api, refreshCsrf } from './api';
afterEach(() => vi.unstubAllGlobals());
describe('API session boundaries', () => {
  it('sends the server CSRF token on mutations and same-origin credentials', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({token:'server-token'}),{status:200}))
      .mockResolvedValueOnce(new Response(null,{status:204}));
    vi.stubGlobal('fetch', fetch);
    await refreshCsrf(); await api('/auth/logout','POST');
    expect(fetch.mock.calls[1][1].headers['X-CSRF-TOKEN']).toBe('server-token');
    expect(fetch.mock.calls[1][1].credentials).toBe('same-origin');
  });
  it('does not treat unauthorized responses as successful data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null,{status:403})));
    await expect(api('/admin/audit')).rejects.toThrow('Permesso non concesso');
  });
});
