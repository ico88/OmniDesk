using System.Diagnostics;
using System.Security.Principal;
using OmniDesk.Contracts;
using OmniDesk.Windows.Ipc;

namespace OmniDesk.Tray.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { MessageBox.Show("Avvia OmniDesk Tray senza privilegi amministrativi.", "OmniDesk Tray"); return; }
        using var singleton = new Mutex(true, @"Local\OmniDesk.Tray.v1", out var created);
        if (!created) return;
        ApplicationConfiguration.Initialize();
        using var context = new TrayContext();
        Application.Run(context);
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon icon;
    private readonly System.Drawing.Icon brandIcon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new("Connessione in verifica…") { Enabled = false };
    private readonly ToolStripMenuItem heartbeat = new("Nessun heartbeat noto") { Enabled = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15000 };
    private readonly CancellationTokenSource stop = new();
    private bool polling;
    public TrayContext()
    {
        menu.Items.Add(status); menu.Items.Add(heartbeat);
        var refresh = new ToolStripMenuItem("Aggiorna stato"); refresh.Click += async (_, _) => await Poll(); menu.Items.Add(refresh);
        var support = new ToolStripMenuItem("Richiedi assistenza / I miei ticket");
        support.Click += (_, _) => { using var form = new SupportForm(); form.ShowDialog(); };
        menu.Items.Add(support);
        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("Chiudi icona"); exit.Click += (_, _) => ExitThread(); menu.Items.Add(exit);
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("OmniDesk.Tray.Windows.Assets.OmniDesk.ico") ?? throw new InvalidOperationException("OmniDesk icon resource is missing."))
            brandIcon = new System.Drawing.Icon(stream);
        icon = new NotifyIcon { Icon = brandIcon, Visible = true, Text = "OmniDesk — verifica connessione", ContextMenuStrip = menu };
        timer.Tick += async (_, _) => await Poll(); timer.Start();
        _ = Poll();
    }
    private async Task Poll()
    {
        if (polling || stop.IsCancellationRequested) return;
        polling = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var response = await Task.Run(async () => {
                using var pipe = await LocalStatusPipe.OpenVerifiedAsync(Process.GetCurrentProcess().SessionId, timeout.Token);
                var request = new TrayRequest(TrayProtocol.Version, Guid.NewGuid(), TrayProtocol.StatusMethod);
                await TrayProtocol.Write(pipe, request, timeout.Token);
                var reply = await TrayProtocol.Read<TrayStatus>(pipe, timeout.Token);
                if (!TrayProtocol.Valid(reply, request.RequestId)) throw new InvalidDataException("Invalid Agent response.");
                return reply;
            }, timeout.Token);
            if (stop.IsCancellationRequested) return;
            var state = response.State == "online" && (response.LastHeartbeat is null || response.LastHeartbeat < DateTimeOffset.UtcNow.AddMinutes(-3)) ? "offline" : response.State;
            var text = state switch {
                "online" => "Connesso al server", "not_enrolled" => "Dispositivo non registrato", "auth_rejected" => "Autenticazione dispositivo rifiutata",
                "degraded" => "Connessione recente, invio dati incompleto", "connecting" or "starting" => "Connessione in verifica", _ => "Server non raggiungibile"
            };
            status.Text = text;
            heartbeat.Text = response.LastHeartbeat is null ? "Nessun heartbeat confermato" : "Ultimo heartbeat: " + response.LastHeartbeat.Value.ToLocalTime().ToString("g");
            icon.Text = "OmniDesk — " + text;
        }
        catch (Exception) when (!stop.IsCancellationRequested)
        {
            status.Text = "Servizio non disponibile o non verificato";
            heartbeat.Text = "Stato server non disponibile";
            icon.Text = "OmniDesk — servizio non disponibile";
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
        finally { polling = false; }
    }
    protected override void ExitThreadCore()
    {
        stop.Cancel(); timer.Stop(); icon.Visible = false; base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); timer.Dispose(); icon.Dispose(); brandIcon.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}
