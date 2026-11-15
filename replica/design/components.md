# Componenti OmniDesk (nuovo)

Stato: **progettato**. Primitive di prova in `preview.html` (HTML statico con
`tokens.css`); i componenti React veri nasceranno nel nuovo repository.

Basi: React + **Radix UI** (MIT: dialog, menu, popover, tabs, tooltip,
toggle, accessibili per default), icone **Lucide** (ISC), font **IBM Plex**
(OFL) incluso nel bundle. Assist sul PC: WPF con gli stessi ruoli colore,
font Segoe UI di sistema. Nessun asset o testo di TeamViewer: ogni etichetta
qui è scritta da zero.

Regole comuni:
- solo token (`bg-surface text-muted`), mai hex o px nei componenti;
- focus visibile su tutto: anello 2px `focus`, offset 2px;
- bersaglio minimo 32px (24px solo nella toolbar densa, con spazio intorno);
- stati sempre progettati: default, hover, active, focus-visible, disabled,
  loading, error, empty;
- testi in italiano, brevi, con il verbo ("Connetti", non "Connessione").

ID schermate dalla [recon](../recon.md).

## Base

```
Button
  variants  primary, secondary, ghost, danger
  sizes     sm 32px, md 36px, lg 44px
  states    default, hover (accent-hover), active, focus-visible, disabled (opacità 50%, cursore not-allowed), loading (spinner a sinistra, etichetta resta)
  tokens    primary: bg accent, text on-accent; secondary: bg surface-2, text text, border border; ghost: trasparente, text text, hover surface-2; danger: bg danger, text on-danger
            radius md, font sm/600, padding x space-4
  a11y      <button> vero; loading = aria-busy e disabled, etichetta letta comunque; danger mai l'unico segnale (testo esplicito)
  used on   tutte
```

```
IconButton
  variants  ghost, toolbar (su dark surface-2)
  sizes     sm 28px, md 32px
  states    come Button + pressed (toggle: bg accent-soft, icona on-accent-soft)
  tokens    radius md, icona 16/20px
  a11y      aria-label obbligatorio, Tooltip con lo stesso testo e scorciatoia; toggle = aria-pressed
  used on   S08, S09, S10, S12
```

```
TextField
  variants  text, password (mostra/nascondi), search (icona, Esc svuota)
  sizes     md 36px
  states    default, hover, focus-visible (bordo focus + anello), disabled, read-only, error (bordo danger + messaggio sotto)
  tokens    bg bg, border border-input, text text, placeholder text-muted, radius md, font sm
  a11y      <label> sempre visibile (niente solo placeholder); errore collegato con aria-describedby e aria-invalid
  used on   login, S01, S02, S07, S16, S17, admin
```

```
CodeInput
  scopo     inserire il codice di assistenza in Assist (S05)
  formato   9 cifre in 3 gruppi da 3 ("482 913 507"), incolla accetta spazi e trattini
  states    vuoto, parziale, completo (Continua abilitato), verifica (loading), errato ("Codice non valido o scaduto"), bloccato ("Troppi tentativi. Riprova tra 2 minuti", conto alla rovescia)
  tokens    font mono, type code, bg bg, border border-input
  a11y      un solo <input inputmode="numeric" autocomplete="one-time-code">, non nove caselle; messaggi in aria-live polite
  used on   S05
```

```
CodeDisplay
  scopo     mostrare al tecnico codice e link dell'invito, una sola volta (S02)
  parti     codice grande, pulsante Copia codice, link troncato + Copia link, scadenza ("Scade alle 15:40")
  states    visibile, copiato (icona check per 2 s, annuncio "Copiato"), scaduto (codice barrato, "Invito scaduto", pulsante Crea nuovo)
  tokens    type code, font mono, bg surface, radius lg
  a11y      il codice è testo selezionabile; Copia annunciato con aria-live
  used on   S02, S03
```

```
Select, Checkbox, Switch, RadioGroup
  base      Radix Select / Checkbox / Switch / RadioGroup
  sizes     controllo 36px (select), 18px (checkbox), 20x36px (switch)
  states    default, hover, focus-visible, checked, disabled, error
  tokens    selezionato accent; bordo border-input
  a11y      etichetta cliccabile; Switch annuncia stato
  used on   S16, S17, admin
```

```
Badge / StatusDot
  variants  online (success), offline (text-muted, contorno), in sessione (live), registrazione (recording), revocato (danger), non assistito consentito (accent-soft)
  sizes     dot 8px; badge 20px, font xs/500
  a11y      mai solo colore: il dot ha sempre testo accanto o aria-label ("Online")
  used on   S08, S09, S18
```

```
Toast
  variants  info, success, warning, error
  states    entra, resta (5 s; error resta finché chiuso), chiudi
  tokens    bg surface-2, bordo sinistro 3px del ruolo, shadow pop, z toast
  a11y      role=status (info/success) o alert (error); pausa al passaggio del mouse e al focus
  used on   tutte
```

```
Dialog
  base      Radix Dialog
  variants  normale, conferma distruttiva (pulsante danger, testo che dice cosa succede: "Revoca il dispositivo PC-UFFICIO-03. Le sessioni aperte verranno chiuse.")
  states    aperto, invio in corso (pulsanti disabilitati), errore in linea
  tokens    bg bg, radius lg, shadow pop, larghezza 440px (max 100vw - 32px)
  a11y      focus intrappolato, Esc chiude (non durante invio), focus torna al pulsante che l'ha aperto, titolo = aria-labelledby
  used on   admin, S16, S17, S08 (revoca)
```

```
EmptyState
  parti     icona Lucide 24px text-muted, titolo, una riga, un'azione
  esempi    S08 senza dispositivi: "Nessun dispositivo registrato" · "Crea un token di registrazione e installa OmniDesk sul primo PC." · [Crea token]
            S18 vuoto: "Nessuna sessione nel periodo scelto" · [Azzera filtri]
            S03 inviti: "Nessun invito aperto" · [Nuovo invito]
  used on   S08, S18, S03, admin
```

```
Skeleton
  scopo     caricamento di liste e dettagli (niente spinner a pagina intera)
  tokens    bg surface-2, animazione disattivata con prefers-reduced-motion
  used on   S08, S18
```

## Console

```
AppShell
  parti     sidebar 232px (logo testuale, Dispositivi, Assistenza, Report, Amministrazione se permesso), header 52px (ricerca, utente, tema)
  states    sidebar compressa sotto md (icone + tooltip), menu a scomparsa sotto sm
  a11y      <nav> con aria-label, link attivo aria-current="page", "Salta al contenuto" come primo elemento
```

```
DeviceTable (S08)
  colonne   stato, nome, gruppo, utente collegato, ultimo contatto, azioni
  riga      hover surface; azione principale Connetti (primary sm) visibile se permesso; menu ⋯ con Solo visione, Accendi (WoL), Dettagli
  states    caricamento (skeleton 8 righe), vuoto (EmptyState), errore ("Impossibile caricare i dispositivi" + Riprova), filtrato senza risultati, offline (Connetti disabilitato con tooltip "Il PC non è raggiungibile"), già in sessione (badge "In sessione con M. Rossi", Connetti → "Partecipa")
  mobile    sotto sm diventa lista di card
  a11y      <table> vera; azioni con nome del dispositivo nell'aria-label ("Connetti a PC-UFFICIO-03")
```

```
FilterBar (S08, S18)
  parti     ricerca, gruppo, stato, intervallo date (S18), Azzera
  a11y      i filtri aggiornano la tabella e annunciano "12 risultati"
```

```
ReportTable (S18)
  colonne   inizio (ora locale, UTC in tooltip), durata, tecnico, dispositivo, modalità, esito, registrazione
  azioni    riga apre la timeline eventi; Esporta CSV (UTC) in alto
  states    vuoto, caricamento, paginazione (50 per pagina)
```

```
PolicyEditor (S17)
  parti     sezioni Accesso, Sessione, File, Registrazione; ogni regola = etichetta + spiegazione in una riga + controllo
  states    modificato non salvato (barra in basso "Modifiche non salvate" + Salva/Annulla), salvataggio, errore di validazione per campo
  regole    "Chiedi il consenso dell'utente" (switch); "Consenti accesso senza utente presente" (switch). Possono essere attivi entrambi; almeno uno deve esserlo (vincolo anche nel DB): se si spengono tutti e due, errore in linea "Serve almeno una delle due modalità di accesso"
  a11y      ogni switch ha la spiegazione in aria-describedby
```

```
Timeline (S18 dettaglio)
  parti     elenco verticale eventi con ora, icona, attore, testo ("M. Rossi ha inviato Ctrl+Alt+Del")
  note      mai contenuti di appunti o chat, solo il fatto
```

## Viewer di sessione (S09, tema dark sempre)

```
SessionViewport
  parti     <canvas> centrato su bg dark, cursore remoto disegnato sopra
  scala     Adatta (default), Originale (100%, scroll), Riempi
  states    connecting ("Connessione a PC-UFFICIO-03…"), consent_pending ("In attesa che l'utente accetti" + conto alla rovescia + Annulla), active, reconnecting (frame congelato desaturato + "Riconnessione…"), reboot_pending ("Il PC si sta riavviando. La sessione riprenderà da sola."), declined ("L'utente ha rifiutato la sessione"), ended (motivo in chiaro + Riconnetti / Chiudi), error
  input     cattura tastiera solo con focus sul canvas; Esc lungo (1 s) o scorciatoia rilascia la tastiera; indicatore "Tastiera inviata al PC remoto"
  a11y      il canvas ha aria-label con nome PC e stato; gli overlay di stato sono role=status
```

```
SessionToolbar
  posizione barra in alto 40px, centrata, comprimibile in una linguetta; trascinabile orizzontalmente
  gruppi    [Azioni] Ctrl+Alt+Del · Blocca · Riavvia ▾ (Riavvia, Riavvia in modalità provvisoria) · Schermo nero · Blocca input
            [Vista] Monitor ▾ (1, 2, Tutti) · Scala ▾ · Qualità ▾ (Automatica, Fluida, Nitida)
            [Strumenti] File · Chat · Appunti ▾ · Screenshot · Registra
            [Sessione] Partecipanti · Info · Termina (danger)
  regole    pulsanti non consentiti dalla policy: nascosti, non solo disabilitati, con voce "Non consentito dalla policy" nel menu Info
  states    normale, compressa, a schermo intero, azione in corso (spinner sul pulsante), azione rifiutata (toast)
  tokens    bg surface-2 (dark), shadow toolbar, radius lg, IconButton toolbar
  a11y      role=toolbar, frecce sinistra/destra tra i pulsanti, ogni pulsante con tooltip e scorciatoia
```

```
SessionTabs
  scopo     più sessioni nella stessa finestra
  states    attiva, in riconnessione (dot live lampeggiante disattivato con reduced-motion), terminata (barrata, chiudibile)
  a11y      Radix Tabs; Ctrl+Tab tra sessioni non catturato dal PC remoto
```

```
SidePanel (File S10, Chat S12, Partecipanti, Info S13)
  larghezza 360px a destra, sopra il viewport, chiudibile
  File      due colonne Questo computer / PC remoto, trascina per inviare, coda con barra di avanzamento, Annulla, errore per file ("Supera il limite di 100 MB della policy")
  Chat      messaggi con ora; avviso fisso "La chat non viene salvata"
  Info      sistema remoto, connessione (latenza, fps, banda), sessione (inizio, policy)
```

## Assist sul PC dell'utente (WPF)

```
ConsentDialog (S06)
  testo     "Marco Rossi (IT) chiede di collegarsi a questo computer."
            "Potrà vedere lo schermo e usare mouse e tastiera." (oppure "Potrà solo vedere lo schermo.")
            "La sessione verrà registrata." solo se vero
  azioni    [Consenti] primary · [Rifiuta] secondary · conto alla rovescia visibile ("Rifiutata automaticamente tra 45 s")
  regole    sempre in primo piano, centrata sul monitor principale; non si chiude con un clic fuori; nessun pulsante di default (Invio non accetta per sbaglio mentre l'utente sta scrivendo): serve un clic o Tab + Spazio
  a11y      UI Automation: nome e descrizione letti dall'assistente vocale
```

```
SessionIndicator
  scopo     far sempre sapere all'utente che qualcuno è collegato
  parti     barra 32px in alto al centro del monitor principale: dot live, "Marco Rossi è collegato", badge "Registrazione" se attiva, [Chat], [Termina]
  regole    non si può chiudere né nascondere da policy; resta sopra le altre finestre; trascinabile lungo il bordo superiore
  tokens    bg live, testo on-live; badge recording / on-recording
  a11y      annuncio vocale all'inizio e alla fine della sessione
```

```
Assist (finestra principale, S05)
  parti     stato del PC ("Gestito da OmniDesk · Connesso"), campo CodeInput "Hai ricevuto un codice dal tecnico?", pulsante Continua
  states    connesso, non connesso al server ("Impossibile raggiungere il server OmniDesk"), in sessione (mostra SessionIndicator e Termina)
```

## Componenti della recon non necessari

Whiteboard, audio/video, AR, stampa e VPN: fuori ambito (vedi
`features.csv`), nessun componente.
