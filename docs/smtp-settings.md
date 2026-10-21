# Configurazione SMTP

Un amministratore OmniDesk può configurare la posta in uscita dalla voce **Email SMTP**.

1. Inserire host e porta del provider: `587` usa STARTTLS, `465` usa TLS implicito.
2. Inserire mittente, username e password SMTP. La password resta vuota nel modulo anche dopo il salvataggio; vuoto mantiene quella già memorizzata. Per rimuoverla usare **Rimuovi la password salvata**.
3. Impostare l'URL HTTPS pubblico di OmniDesk, usato nei collegamenti delle notifiche.
4. Selezionare **Abilita invio email**, salvare e inviare un messaggio di prova a un indirizzo controllato.

La configurazione è cifrata con ASP.NET Data Protection e la password non viene restituita all'interfaccia. Se è già presente una configurazione server via environment, la schermata la mostra come origine iniziale; al primo salvataggio la configurazione del pannello prende precedenza. Gli worker rileggono i dati salvati mentre operano, senza riavvio. Gli invii accodati durante una pausa SMTP restano in attesa finché l'invio non viene riattivato.

Questa schermata abilita la posta in uscita per notifiche e flussi email già predisposti. Non abilita la 2FA email: il relativo flag resta separato e disattivato finché non viene attivato esplicitamente nella configurazione di sicurezza.

Il test usa il provider SMTP configurato e invia davvero il messaggio al destinatario scelto. Eseguirlo solo verso una casella autorizzata.
