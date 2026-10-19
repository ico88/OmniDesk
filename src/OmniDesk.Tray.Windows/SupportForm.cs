using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using OmniDesk.Contracts;
using OmniDesk.Windows.Ipc;

namespace OmniDesk.Tray.Windows;

internal sealed class SupportForm : Form
{
    private readonly TextBox subject = new() { MaxLength = 200, Dock = DockStyle.Top, PlaceholderText = "Oggetto" };
    private readonly TextBox description = new() { MaxLength = 8000, Multiline = true, Height = 130, Dock = DockStyle.Top, PlaceholderText = "Descrivi il problema" };
    private readonly CheckBox consent = new() { Text = "Acconsento ad allegare l'immagine mostrata al team IT", AutoSize = true, Dock = DockStyle.Top };
    private readonly PictureBox preview = new() { Height = 170, Dock = DockStyle.Top, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly TextBox tickets = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label result = new() { Dock = DockStyle.Bottom, Height = 45 };
    private readonly Button send = new() { Text = "Invia richiesta", AutoSize = true };
    private readonly Button capture = new() { Text = "Acquisisci schermata", AutoSize = true };
    private readonly Button remove = new() { Text = "Rimuovi schermata", AutoSize = true };
    private readonly Button refresh = new() { Text = "Aggiorna ticket", AutoSize = true };
    private byte[]? screenshot;
    private SupportCreate? pending;
    private bool busy;
    private readonly CancellationTokenSource stop = new();

    public SupportForm()
    {
        Text = "OmniDesk — Richiedi assistenza"; Width = 660; Height = 750; StartPosition = FormStartPosition.CenterScreen;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 65 };
        buttons.Controls.AddRange([capture, remove, send, refresh]);
        Controls.Add(tickets); Controls.Add(buttons); Controls.Add(consent); Controls.Add(preview); Controls.Add(description); Controls.Add(subject); Controls.Add(result);
        capture.Click += (_, _) => CaptureScreen(); remove.Click += (_, _) => ClearImage();
        send.Click += async (_, _) => await Send(); refresh.Click += async (_, _) => await List();
        Shown += async (_, _) => await List();
    }
    private void ClearImage() { screenshot = null; preview.Image?.Dispose(); preview.Image = null; consent.Checked = false; pending = null; }
    private void CaptureScreen()
    {
        if (MessageBox.Show(this, "Verrà acquisito lo schermo principale della tua sessione. Chiudi o nascondi prima i dati riservati. Continuare?", "Screenshot", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Hide();
        try
        {
            var bounds = Screen.PrimaryScreen!.Bounds;
            using var original = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(original)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            ClearImage();
            for (var maxWidth = 1280; maxWidth >= 320; maxWidth /= 2)
            {
                var scale = Math.Min(1d, Math.Min((double)maxWidth / original.Width, 720d / original.Height));
                using var resized = new Bitmap(Math.Max(1,(int)(original.Width*scale)),Math.Max(1,(int)(original.Height*scale)),PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(resized)) g.DrawImage(original,0,0,resized.Width,resized.Height);
                using var stream = new MemoryStream(); resized.Save(stream,ImageFormat.Png);
                if (stream.Length <= 524288) { screenshot = StripMetadata(stream.ToArray()); preview.Image = new Bitmap(resized); break; }
            }
            result.Text = screenshot is null ? "Immagine troppo grande. Richiesta inviabile senza screenshot." : "Verifica l'anteprima e seleziona il consenso prima dell'invio.";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        { ClearImage(); result.Text = "Impossibile acquisire lo schermo della sessione."; }
        finally { Show(); Activate(); }
    }
    private static byte[] StripMetadata(byte[] png)
    {
        using var output = new MemoryStream(); output.Write(png,0,8);
        var position = 8;
        while (position <= png.Length-12)
        {
            var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position,4));
            if (size < 0 || size > png.Length-position-12) throw new InvalidDataException();
            var kind = System.Text.Encoding.ASCII.GetString(png,position+4,4);
            if (kind is "IHDR" or "IDAT" or "IEND") output.Write(png,position,size+12);
            position += size+12;
        }
        return output.ToArray();
    }
    private async Task<TraySupportResponse> Call(TrayRequest request)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        return await Task.Run(async () => {
            using var pipe = LocalStatusPipe.OpenVerified(Process.GetCurrentProcess().SessionId);
            await TrayProtocol.Write(pipe,request,timeout.Token,TrayProtocol.MaxSupportPayload);
            var response = await TrayProtocol.Read<TraySupportResponse>(pipe,timeout.Token,TrayProtocol.MaxSupportPayload);
            if (response.Version != TrayProtocol.Version || response.RequestId != request.RequestId) throw new InvalidDataException();
            return response;
        },timeout.Token);
    }
    private async Task Work(Func<Task> action)
    {
        if (busy) return; busy = true;
        send.Enabled = refresh.Enabled = capture.Enabled = remove.Enabled = subject.Enabled = description.Enabled = consent.Enabled = false;
        try { await action(); }
        catch (Exception) when (!stop.IsCancellationRequested) { result.Text = "Servizio non disponibile. Riprova tra qualche secondo."; }
        catch (Exception) when (stop.IsCancellationRequested) { }
        finally
        {
            busy = false;
            if (!IsDisposed) send.Enabled = refresh.Enabled = capture.Enabled = remove.Enabled = subject.Enabled = description.Enabled = consent.Enabled = true;
        }
    }
    private Task List() => Work(async () => {
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.ListTicketsMethod));
        if (stop.IsCancellationRequested) return;
        result.Text = response.Error ?? "Ticket aggiornati";
        if (response.Error is null) tickets.Text = string.Join(Environment.NewLine + Environment.NewLine,response.Tickets.Select(t => $"{t.CreatedAt:g} — {t.Subject}\r\nStato: {t.State}\r\n{t.Resolution}"));
    });
    private Task Send() => Work(async () => {
        if (string.IsNullOrWhiteSpace(subject.Text) || string.IsNullOrWhiteSpace(description.Text)) { result.Text = "Compila oggetto e descrizione."; return; }
        if (screenshot is not null && !consent.Checked) { result.Text = "Per allegare lo screenshot occorre il consenso; puoi anche rimuoverlo."; return; }
        var image = screenshot is null ? null : Convert.ToBase64String(screenshot);
        if (pending is null || pending.Subject != subject.Text || pending.Description != description.Text || pending.ScreenshotBase64 != image)
            pending = new(Guid.NewGuid(),subject.Text,description.Text,image,consent.Checked);
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.CreateTicketMethod,pending));
        if (stop.IsCancellationRequested) return;
        result.Text = response.Error ?? $"Richiesta registrata: {response.Tickets[0].Id}";
        if (response.Error is null) { pending = null; subject.Clear(); description.Clear(); ClearImage(); }
    });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); preview.Image?.Dispose(); stop.Dispose(); }
        base.Dispose(disposing);
    }
}
