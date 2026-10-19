# Agente Windows — M2

Implementazione in `src/OmniDesk.Agent.Windows`, contratti in `src/OmniDesk.Contracts`, MSI WiX in `installer/OmniDesk.Agent`. Vedi [protocollo e limiti](../../docs/milestone-2.md). Binario win-x64 cross-compilato e MSI unsigned compilato in GitHub Actions Windows. Installazione/servizio/upgrade e firma richiedono ancora collaudo; collector, inventario, DPAPI e JWT già verificati sul runner Windows; test ACL in directory temporanea aggiunto nella correzione 0.5.1.

Evoluzione solo progettata: [Agent privilegiato e Tray utente distinti](../../docs/windows-agent-tray.md), con MSI unico futuro, named pipe protetta, Service Recovery, ticket e consenso remoto. Nessun componente Tray o modifica dell'MSI introdotto da questa specifica.

Incremento 0.5.2: [installazione LAN](../../docs/windows-lan-install.md), connect-test e verifica enrollment, client HTTP comune, script PowerShell con CA LocalMachine verificata e MSI elevato. Binario cross-compilato; nuovo MSI e installazione Windows ancora da verificare. Il kit diagnostico non include MSI o motore remoto.

Sorgenti successivi 0.6.0: [Agent+Tray read-only](../../docs/windows-agent-tray-implementation.md), stato heartbeat, pipe per logon e MSI combinato con avvio Tray/recovery/DACL servizio. Non compilati: comandi Docker bloccati dall'indisponibilità della revisione automatica. Il kit 0.5.2 precedente e l'istanza LAN non sono stati aggiornati. Ticket/screenshot/remoto non implementati.
