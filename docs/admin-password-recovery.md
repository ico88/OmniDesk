# Reimpostare la password amministratore

Il file `.secrets/bootstrap_password` contiene solo la password iniziale e non
si aggiorna quando l'utente cambia password. Identity conserva password hash:
non esiste un comando per leggerle in chiaro.

Per reimpostare la password di un account Administrator attivo, eseguire dalla
directory del progetto sul server:

```bash
sg docker -c 'bash scripts/reset-admin-password.sh admin@example.test'
```

Lo script chiede la nuova password senza mostrarla, la conferma e la passa al
container API su standard input, senza file temporanei o argomenti contenenti
la password. L'API usa ASP.NET Identity, invalida le sessioni con il cambio del
security stamp e registra `user.password.reset.cli` senza includere la password.
L'operazione è limitata ad account Administrator attivi; non cambia ruoli o
stato account. Se la sessione Codex/terminale ha gruppi Docker non aggiornati,
`sg docker -c` li carica per il comando.

Per consultare la sola password iniziale, se il file esiste ancora:

```bash
sudo cat .secrets/bootstrap_password
```
