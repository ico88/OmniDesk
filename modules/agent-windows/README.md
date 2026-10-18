# Agente Windows — M2

Implementazione in `src/OmniDesk.Agent.Windows`, contratti in `src/OmniDesk.Contracts`, MSI WiX in `installer/OmniDesk.Agent`. Vedi [protocollo e limiti](../../docs/milestone-2.md). Binario win-x64 cross-compilato e MSI unsigned compilato in GitHub Actions Windows. Installazione/servizio/upgrade e firma richiedono ancora collaudo; collector, inventario, DPAPI e JWT già verificati sul runner Windows; test ACL in directory temporanea aggiunto nella correzione 0.5.1.

Evoluzione solo progettata: [Agent privilegiato e Tray utente distinti](../../docs/windows-agent-tray.md), con MSI unico futuro, named pipe protetta, Service Recovery, ticket e consenso remoto. Nessun componente Tray o modifica dell'MSI introdotto da questa specifica.
