# Build log

Codice in [ico88/omnidesk-next](https://github.com/ico88/omnidesk-next), branch `sviluppo`.
Esiti e limiti dettagliati: `docs/validation.md` di quel repository.

| ID | data | stato | cosa manca | più difficile del previsto |
| --- | --- | --- | --- | --- |
| M0 shell | 2026-11-15 | fatto | — | CSP e dialog Radix (stile inline); etichette sr-only che causavano scroll orizzontale |
| Login | 2026-11-15 | fatto | TOTP (opzionale, non implementata) | token antiforgery da riemettere dopo il login |
| Utenti, gruppi, audit, account | 2026-11-15 | fatto | concessioni per gruppo/dispositivo solo via API (manca la schermata) | unicità case-insensitive dei nomi con ILIKE senza jolly |
| S08 Dispositivi | 2026-11-15 | fatto (M1): elenco con stato online, utente collegato, ricerca, filtro gruppo, revocati; dettaglio, rinomina/sposta, revoca; token di registrazione | pulsante Connetti (M2) | ordinamento EF dopo proiezione su record; chiusura WebSocket già chiusa dal server |
| S09 Sessione | 2026-11-15 | fatto (M2): viewer WebCodecs, mouse/tastiera, schermo intero, Termina, stati | toolbar completa, multi-monitor, Ctrl+Alt+Canc (M3); collaudo su PC Windows reale | Vortice senza ICodecAPI (interfaccia COM scritta a mano); D3D11CreateDevice ambiguo; corsa nel test dei ticket |
| S01–S03 Assistenza | 2026-11-16 | fatto (M4b): inviti, PC in attesa, Avvia, revoca; pagina pubblica di download; programma portatile | firma del programma per uso esterno | — |
| S17 Policy | 2026-11-15 | fatto (M3a): elenco con regole in chiaro, crea/modifica, assegna ai gruppi, predefinita, elimina con conferma; policy effettiva nel dettaglio PC | assegnazione a singoli PC dalla console; appunti/file (M3b) | WebSocket chiuso annullando prima la ricezione: Bye perso 5/10, corretto |
| S10 File | 2026-11-15 | fatto (M3b): pannello nella sessione, sfoglia, scarica, invia con trascinamento, avanzamento | file box (could) | eco degli appunti su Windows (doppia notifica), corretto |
| S18 Report | 2026-11-15 | fatto (M3a): filtri per data, export CSV UTC | filtro per PC in console | — |

Dopo M4b la parità è 51,0/100: assistenza con invito e programma portatile, Wake-on-LAN; più tecnici e registrazioni esclusi dall'utente. Dopo M4a la parità è 44,2/100: chat, qualità, schermo nero escluso dalla cattura e blocco input (policy, spenti di default), screenshot e note; schermo nero e chat provati su Windows in CI. Dopo M3b la parità è 34,6/100 (6 must-have su 21): appunti per direzione, file nei due sensi (come utente collegato, SHA-256), riavvio con ripresa; provati nel browser con host di prova, appunti anche su Windows in CI. Collaudo su PC reale da fare. Dopo M3a la parità è 27,4/100 (5 must-have su 21): consenso, indicatore, Ctrl+Alt+Canc, multi-monitor e blocco a fine sessione provati con host di prova nel browser e con autodiagnosi su Windows in CI; collaudo su PC reale da fare. Dopo M2 la parità calcolata da parity.py è 18,8/100 (3 must-have su 21 fatti, altri parziali in attesa del collaudo Windows). Dopo M1 nessuna riga di `features.csv` passa a `yes`: la riga "Stato online/offline e disponibilità remota per dispositivo" resta `no` finché non esiste la parte "disponibilità remota" (M2). Nessuna riga passa a `yes` o `partial`: M0 costruisce le
fondamenta (accesso, permessi, audit, protocollo, deployment), non ancora una
funzione di connessione remota. Le schermate future non mostrano dati
d'esempio, per non far sembrare operativo ciò che non lo è.

Non verificato in questo ambiente (nessun daemon Docker): build dell'immagine e
`setup.sh --start` con Caddy e Let's Encrypt.
