# Accesso non presidiato per dispositivo

Il consenso locale rimane predefinito. Dalla scheda dispositivo, nella sezione
Accesso remoto, un amministratore globale puo abilitare Accesso non presidiato.
La modifica richiede conferma, versione policy corrente e credenziale dispositivo
attiva; viene registrata nell'audit. Non cambia le concessioni: desktop richiede
remote.control, terminale terminal, file files, negli ambiti gia autorizzati.

Il gruppo Mesh gestito conserva notifiche per tutti i protocolli (mask7). Ogni
link temporaneo autorizzato dal server specifica il proprio consenso: notifiche
piu richiesta locale in modalita predefinita, sole notifiche in non presidiata.
Nessun flag controllabile dal richiedente puo saltare la policy server. Gli
utenti Windows standard non possono modificarla dalla Tray. Non viene abilitato
un login pubblico o condiviso l'account del motore.

La modalita e fotografata nello storico della sessione. Una modifica della
policy mette le sessioni precedenti in revoca, confermata dal motore con retry
ogni10s. La chiusura non e dichiarata istantanea in caso di motore irraggiungibile.
Le creazioni concorrenti ricontrollano versione e autorizzazione prima di
restituire il link. Riabilitare un motore disattivato ripristina il consenso.

Per il collaudo: chiudere le sessioni precedenti, abilitare la policy sulla sola
macchina pilota, avviare una nuova sessione e verificare notifica senza richiesta;
provare poi schermata bloccata, riavvio senza logon e accessi terminale/file
separati. Disabilitare la policy e verificare revoca e ritorno della richiesta
sulla sessione successiva. I test CI non attestano questi casi interattivi.

Le nuove sessioni desktop richiedono anche la barra di connessione del motore
(flag64), oltre al toast iniziale: mask73 con consenso, mask65 senza richiesta.
Terminale/file conservano notifiche separate. La barra dipende dalla sessione
interattiva e dal supporto Windows del motore; non promette un popup su desktop
bloccato o senza utente. Verificare comparsa all'apertura e rimozione alla chiusura
sul PC pilota; i link gia emessi non acquisiscono retroattivamente il flag.
