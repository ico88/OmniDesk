using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace OmniDesk.Setup.Windows;

internal static class Program
{
    private const string Server = "https://192.168.1.233:8085";
    private const string CaSha256 = "45926C7ACA3D49E6619E377AD0FA2110951F6F0E54DF9B83A6C6E3AD712BE5B2";
    private static readonly string ProgramFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OmniDesk Agent");
    private static readonly string Agent = Path.Combine(ProgramFolder, "OmniDesk.Agent.Windows.exe");
    private static readonly string Identity = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OmniDesk", "agent.json");

    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Per installare OmniDesk serve un amministratore Windows.", "OmniDesk Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }

    private sealed class SetupForm : Form
    {
        private readonly TextBox token = new() { UseSystemPasswordChar = true, MaxLength = 64, PlaceholderText = "Token monouso dalla console OmniDesk" };
        private readonly Button install = new() { Text = "Installa OmniDesk", AutoSize = true };
        private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(580, 70) };
        private bool busy;
        public SetupForm()
        {
            Text = "OmniDesk Setup"; Width = 640; Height = 340; MinimumSize = new Size(640, 340);
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("OmniDesk.Setup.Windows.Assets.OmniDesk.ico") ?? throw new InvalidOperationException("OmniDesk icon resource is missing."))
                Icon = new Icon(stream);
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "OmniDesk", Font = new Font(Font.FontFamily, 22, FontStyle.Bold), AutoSize = true });
            layout.Controls.Add(new Label { Text = "Agent di sistema e Tray per questa postazione", AutoSize = true, ForeColor = SystemColors.GrayText });
            layout.Controls.Add(new Label { Text = "Server", AutoSize = true, Padding = new Padding(0, 14, 0, 0) });
            layout.Controls.Add(new Label { Text = Server, AutoSize = true });
            layout.Controls.Add(new Label { Text = "Token monouso · Console > Dispositivi > Registra un dispositivo", AutoSize = true, Padding = new Padding(0, 14, 0, 0) });
            token.Dock = DockStyle.Top; layout.Controls.Add(token);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 14, 0, 0) };
            footer.Controls.Add(install); footer.Controls.Add(status); layout.Controls.Add(footer);
            Controls.Add(layout); AcceptButton = install;
            install.Click += async (_, _) => await InstallAsync();
        }

        private async Task InstallAsync()
        {
            if (busy) return;
            var msi = Path.Combine(AppContext.BaseDirectory, "OmniDesk.Agent.msi");
            var caPath = Path.Combine(AppContext.BaseDirectory, "omnidesk-local-ca.crt");
            var isEnrolled = File.Exists(Identity);
            var secret = token.Text.Trim();
            if (!isEnrolled && !Regex.IsMatch(secret, "\\A[0-9A-Fa-f]{64}\\z"))
            { status.Text = "Incolla il token di 64 caratteri generato dalla console."; token.Focus(); return; }
            if (!File.Exists(msi) || !File.Exists(caPath))
            { status.Text = "Nel kit mancano OmniDesk.Agent.msi o la CA pubblica."; return; }
            busy = true; install.Enabled = false; token.Enabled = false;
            try
            {
                status.Text = "Verifica del certificato e del server…";
                await Task.Run(() => TrustPinnedCa(caPath));
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) })
                using (var response = await client.GetAsync(new Uri(new Uri(Server), "api/health/ready")))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Il server OmniDesk non è pronto.");
                    var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    if (!json.TryGetProperty("status", out var serverStatus) || serverStatus.GetString() != "ready") throw new InvalidOperationException("Il server OmniDesk non è pronto.");
                }
                status.Text = "Installazione dei componenti…";
                var installResult = await RunAsync(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ["/i", msi, "/qn", "/norestart"]);
                if (installResult.Code is not (0 or 3010)) throw new InvalidOperationException($"Windows Installer non ha completato l'installazione (codice {installResult.Code}).");
                if (!isEnrolled)
                {
                    status.Text = "Registrazione protetta del dispositivo…";
                    var enrollResult = await RunAsync(Agent, ["enroll", "--server", Server, "--token-stdin"], secret);
                    if (enrollResult.Code != 0) throw new InvalidOperationException("Registrazione non riuscita. Controlla che il token sia ancora valido e non sia già stato usato.");
                }
                var verify = await RunAsync(Agent, ["verify-enrollment", "--server", Server]);
                if (verify.Code != 0) throw new InvalidOperationException("Il dispositivo non ha completato la registrazione sul server.");
                using (var service = new System.ServiceProcess.ServiceController("OmniDeskAgent"))
                {
                    service.Refresh();
                    if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running) service.Start();
                    service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }
                MessageBox.Show(this, "OmniDesk è installato. Il servizio continua a funzionare anche quando chiudi la Tray.", "Installazione completata", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException)
            { status.Text = ex.Message; }
            finally { secret = string.Empty; token.Clear(); token.Enabled = true; install.Enabled = true; busy = false; }
        }
    }

    private static void TrustPinnedCa(string path)
    {
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
        if (certificate.HasPrivateKey || !certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(x => x.CertificateAuthority) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(certificate.RawData)), CaSha256, StringComparison.Ordinal))
            throw new CryptographicException("La CA del kit non corrisponde all'impronta OmniDesk attesa.");
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0) store.Add(certificate);
    }

    private static async Task<(int Code, string Output)> RunAsync(string exe, string[] arguments, string? standardInput = null)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = standardInput is not null };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Impossibile avviare il componente OmniDesk.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (standardInput is not null) { await process.StandardInput.WriteLineAsync(standardInput); process.StandardInput.Close(); }
        await process.WaitForExitAsync();
        var combined = (await output) + (await error);
        return (process.ExitCode, combined.Length > 2000 ? combined[..2000] : combined);
    }
}
