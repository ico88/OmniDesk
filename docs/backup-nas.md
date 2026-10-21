# Copia notturna dei backup sul NAS Samba

Il timer controlla ogni cinque minuti le impostazioni salvate in OmniDesk.
All'orario scelto, il job crea un backup locale completo con `scripts/backup.sh`,
ne ripete la verifica applicativa, lo cifra con `age` e lo invia alla share
Samba con `smbclient` su SMB3. Conserva il numero di copie impostato in
OmniDesk; elimina quelle più vecchie solo dopo aver caricato e rinominato il
nuovo archivio. Un errore interrompe il job senza applicare la rotazione.

Il backup locale e il trasferimento usano esplicitamente il progetto Compose
`omnidesk-production` con gli overlay `compose.direct.yaml` e
`compose.rustdesk-pilot.yaml`, così operano sui container distribuiti. Se
RustDesk è attivo, il backup locale include anche `/data`, con l'identità
privata del server, e l'overlay necessario a riconoscerne la configurazione.

Gli archivi NAS si chiamano `omnidesk-<UTC timestamp>.tar.gz.age` e contengono
la directory verificata originale, incluso `READY` e i manifest SHA-256. Per
ripristinare, decifrare ed estrarre l'archivio, poi seguire
[backup-restore.md](backup-restore.md). La chiave privata `age` è necessaria:
conservarla offline separata dal server e dal NAS. Il server usa solo il
recipient pubblico.

## Impostazioni OmniDesk

Un amministratore globale apre **Backup server** e configura attivazione,
indirizzo NAS, share/cartella, account Samba, recipient pubblico age, orario,
fuso orario e copie da conservare. La password Samba viene cifrata con ASP.NET
Data Protection e non viene restituita dalla API dopo il salvataggio. Un campo
password vuoto mantiene il valore esistente; è disponibile un'azione distinta
per rimuoverlo. L'audit registra la modifica senza valori o segreti.

L'orario predefinito è 03:00 `Europe/Rome`; lo scheduler controlla ogni cinque
minuti e avvia al primo controllo successivo all'ora impostata. Le impostazioni
sono lette dal database a ogni controllo, quindi la UI controlla destinazione,
attivazione, orario e retention senza modificare file di configurazione host.

## Preparazione host

1. Creare sul NAS una share dedicata e un account Samba dedicato con accesso
   limitato a quella share. Non riutilizzare un account amministratore.
2. Installare `age` e `smbclient` sul server. Il trasferimento usa SMB3
   direttamente e non richiede un mount di sistema.
3. Generare una chiave age su una macchina amministrativa:
   `age-keygen -o omnidesk-backup.agekey`. Custodire il file privato offline e
   inserire in OmniDesk solo il recipient pubblico `age1...`. Non caricare la
   chiave privata sul server o sul NAS.
4. Installare una volta i file systemd
   `deploy/omnidesk-backup-nas.service` e
   `deploy/omnidesk-backup-nas.timer`, poi abilitare il timer. Il timer verifica
   le impostazioni ogni cinque minuti e salta il job se il backup è disattivato
   o non è ancora arrivato l'orario scelto.

Il comando manuale è `scripts/backup-nas.sh` dalla directory del progetto.
Il log systemd si consulta con `journalctl -u omnidesk-backup-nas.service` e il
prossimo controllo con `systemctl list-timers omnidesk-backup-nas.timer`.

Il timer è infrastruttura host e non viene installato automaticamente dall'API.
La prova di ripristino decifrando una copia dal NAS è un collaudo separato.
