# Agente Windows — M2

Implementazione in `src/OmniDesk.Agent.Windows`, contratti in `src/OmniDesk.Contracts`, MSI WiX in `installer/OmniDesk.Agent`. Vedi [protocollo e limiti](../../docs/milestone-2.md). Binario win-x64 cross-compilato e MSI unsigned compilato in GitHub Actions Windows. Installazione/servizio/upgrade e firma richiedono ancora collaudo; self-test read-only Windows aggiunto alla pipeline per collector e DPAPI.
