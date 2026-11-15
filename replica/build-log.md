# Build log

Codice in [ico88/omnidesk-next](https://github.com/ico88/omnidesk-next), branch `sviluppo`.
Esiti e limiti dettagliati: `docs/validation.md` di quel repository.

| ID | data | stato | cosa manca | più difficile del previsto |
| --- | --- | --- | --- | --- |
| M0 shell | 2026-11-15 | fatto | — | CSP e dialog Radix (stile inline); etichette sr-only che causavano scroll orizzontale |
| Login | 2026-11-15 | fatto | TOTP (opzionale, non implementata) | token antiforgery da riemettere dopo il login |
| Utenti, gruppi, audit, account | 2026-11-15 | fatto | concessioni per gruppo/dispositivo solo via API (manca la schermata) | unicità case-insensitive dei nomi con ILIKE senza jolly |
| S08 Dispositivi | 2026-11-15 | fatto (M1): elenco con stato online, utente collegato, ricerca, filtro gruppo, revocati; dettaglio, rinomina/sposta, revoca; token di registrazione | pulsante Connetti (M2) | ordinamento EF dopo proiezione su record; chiusura WebSocket già chiusa dal server |
| S09 Sessione | 2026-11-15 | parziale: rotta | tutto (M2) | — |
| S01–S03 Assistenza | 2026-11-15 | parziale: rotta | tutto (M4) | — |
| S17 Policy | 2026-11-15 | parziale: rotta | tutto (M3) | — |
| S18 Report | 2026-11-15 | parziale: rotta | tutto (M2–M3) | — |

Dopo M1 ancora nessuna riga di `features.csv` passa a `yes`: la riga "Stato online/offline e disponibilità remota per dispositivo" resta `no` finché non esiste la parte "disponibilità remota" (M2). Nessuna riga passa a `yes` o `partial`: M0 costruisce le
fondamenta (accesso, permessi, audit, protocollo, deployment), non ancora una
funzione di connessione remota. Le schermate future non mostrano dati
d'esempio, per non far sembrare operativo ciò che non lo è.

Non verificato in questo ambiente (nessun daemon Docker): build dell'immagine e
`setup.sh --start` con Caddy e Let's Encrypt.
