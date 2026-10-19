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
  it('shows the server explanation when account changes would remove the last administrator', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({error:'Deve rimanere almeno un amministratore attivo.'}),{status:409})));
    await expect(api('/admin/users/user/roles', 'PUT', {roles:[]})).rejects.toThrow('Deve rimanere almeno un amministratore attivo.');
  });
  it('shows the server explanation when a device already has an active intervention', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({code:'job_active',message:'Attendi il completamento e riprova.'}),{status:409})));
    await expect(api('/devices/fixture/services/actions', 'POST', {})).rejects.toThrow('Attendi il completamento e riprova.');
  });
});

it('shows actionable ticket validation errors', async () => {
  vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({error:"Scrivi una risposta all'utente."}),{status:400})));
  await expect(api('/support/fixture','PUT',{})).rejects.toThrow("Scrivi una risposta all'utente.");
});
