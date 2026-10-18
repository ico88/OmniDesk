# Regole di sviluppo OmniDesk

- Implementare per milestone; distinguere progettato, compilato e testato. Non simulare funzionalità operative con dati inventati.
- Architettura modulare: API ASP.NET Core, PostgreSQL, dashboard React/TypeScript; integrazioni e agenti separati.
- Autorizzare sul server ogni operazione. Default deny; separare view, remote.control, terminal, files, updates e administration. Ambiti global, group, device.
- Usare ASP.NET Identity, cookie HttpOnly/Secure/SameSite, antiforgery per mutazioni, password hash e lockout; niente crittografia personalizzata. TOTP disattivata fino a workflow verificato.
- Mai commettere credenziali, token, file .env, certificati o dump. Enrollment futuro: token monouso con hash, scadenza, revoca e identità dispositivo separata.
- Migrazioni database versionate; niente EnsureCreated in produzione. Audit senza segreti.
- Non modificare servizi di sistema/firewall, non usare comandi distruttivi e non fare push senza autorizzazione. setup.sh deve richiedere opt-in per installare Docker.
- Verificare build, test di autenticazione/autorizzazione, migrazioni e configurazione Compose. Registrare comandi e limiti in docs/validation.md.
- Commit separati per documentazione, implementazione, test/correzioni. Nessun obbligo di delega ad altri agenti.
- Prima di pubblicare aggiornare roadmap e istruzioni operative. Gli aggiornamenti dipendenze richiedono nuova verifica.
