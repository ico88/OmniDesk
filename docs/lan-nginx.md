# Deployment LAN precedente (storico)

Queste istruzioni descrivono la configurazione LAN usata prima del trasferimento del servizio. Non usare i valori `192.168.1.233` o `omnidesk.internal` per il nuovo ambiente. L'installazione attuale è pubblica su `https://omnidesk.cricatania.it` con Caddy e ACME; il server previsto usa l'indirizzo interno `10.0.0.53`.

La vecchia configurazione terminava TLS internamente con una CA Caddy e richiedeva un proxy Nginx separato per l'accesso pubblico. I certificati, volumi e procedure di quella installazione non vanno trasferiti. Non importare la vecchia CA, il database o i volumi nella nuova installazione.

Per il deployment corrente seguire le istruzioni in [README](../README.md). L'installer non modifica firewall, routing, SSH o servizi di sistema. La raggiungibilità pubblica di TCP 80/443 va verificata senza cambiare tali impostazioni.
