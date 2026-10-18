using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace OmniDesk.Console.Windows;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Uri Origin(string flag, bool required)
            {
                var index = Array.IndexOf(args, flag);
                if (index < 0 && !required) return null!;
                if (index < 0 || index + 1 >= args.Length || !Uri.TryCreate(args[index + 1], UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new ArgumentException($"{flag} requires an HTTPS origin without credentials or path.");
                return uri;
            }
            Application.Run(new ConsoleWindow(Origin("--server", true), Origin("--mesh", false)));
        }
        catch (Exception e) { MessageBox.Show(e.Message, "OmniDesk Console", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
internal sealed class ConsoleWindow(Uri server, Uri? mesh) : Form
{
    private readonly WebView2 view = new() { Dock = DockStyle.Fill };
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e); Text = "OmniDesk Console"; Width = 1280; Height = 850; MinimumSize = new Size(800, 600); Controls.Add(view);
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniDesk", "Console", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder:data);
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.CoreWebView2.PermissionRequested += (_, request) => request.State = CoreWebView2PermissionState.Deny;
            view.CoreWebView2.ServerCertificateErrorDetected += (_, request) => request.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            view.CoreWebView2.NavigationStarting += (_, request) => { if (!Allowed(request.Uri, server)) request.Cancel = true; };
            view.CoreWebView2.NewWindowRequested += (_, request) => {
                request.Handled = true;
                if (Allowed(request.Uri, server) || (mesh is not null && Allowed(request.Uri, mesh)))
                    Process.Start(new ProcessStartInfo(request.Uri) { UseShellExecute = true });
            };
            view.CoreWebView2.DownloadStarting += (_, request) => request.Cancel = true;
            view.CoreWebView2.Navigate(server.AbsoluteUri);
        }
        catch (Exception exception) { MessageBox.Show(this, "Console initialization failed. Check WebView2 Runtime and certificate trust. " + exception.GetType().Name, "OmniDesk", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private static bool Allowed(string value, Uri origin) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.Host == origin.Host && uri.Port == origin.Port;
    protected override void Dispose(bool disposing) { if (disposing) view.Dispose(); base.Dispose(disposing); }
}
