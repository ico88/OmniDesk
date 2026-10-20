using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using OmniDesk.Contracts;
using OmniDesk.Windows.Ipc;

namespace OmniDesk.Tray.Windows;

internal sealed class SupportForm : Form
{
    private readonly TextBox subject = new() { MaxLength = 200, Dock = DockStyle.Top, PlaceholderText = "Oggetto" };
    private readonly ComboBox category = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox description = new() { MaxLength = 8000, Multiline = true, Height = 130, Dock = DockStyle.Top, PlaceholderText = "Descrivi il problema" };
    private readonly CheckBox consent = new() { Text = "Acconsento ad allegare l'immagine mostrata al team IT", AutoSize = true, Dock = DockStyle.Top };
    private readonly PictureBox preview = new() { Height = 170, Dock = DockStyle.Top, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly ComboBox ticketPicker = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox conversation = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox replyText = new() { MaxLength = 8000, Multiline = true, Dock = DockStyle.Fill, PlaceholderText = "Scrivi un aggiornamento per il team IT" };
    private readonly Label result = new() { Dock = DockStyle.Bottom, Height = 45 };
    private readonly Button send = new() { Text = "Invia richiesta", AutoSize = true };
    private readonly Button capture = new() { Text = "Acquisisci schermata", AutoSize = true };
    private readonly Button remove = new() { Text = "Rimuovi schermata", AutoSize = true };
    private readonly Button refresh = new() { Text = "Aggiorna ticket", AutoSize = true };
    private readonly Button reply = new() { Text = "Invia risposta", AutoSize = true };
    private SupportTicketView? selectedTicket;
    private bool conversationCanReply;
    private byte[]? screenshot;
    private SupportCreate? pending;
    private bool busy;
    private readonly CancellationTokenSource stop = new();

    public SupportForm()
    {
        Text = "OmniDesk — Richiedi assistenza"; Width = 660; Height = 750; StartPosition = FormStartPosition.CenterScreen;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 65 };
        buttons.Controls.AddRange([capture, remove, send, refresh, reply]);
        var thread = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 245, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        thread.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); thread.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        thread.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); thread.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        thread.Controls.Add(ticketPicker, 0, 0); thread.Controls.Add(conversation, 0, 1); thread.Controls.Add(replyText, 0, 2);
        category.Items.AddRange(["Generale", "Hardware", "Software", "Rete", "Account", "Altro"]); category.SelectedIndex = 0;
        Controls.Add(buttons); Controls.Add(thread); Controls.Add(consent); Controls.Add(preview); Controls.Add(description); Controls.Add(category); Controls.Add(subject); Controls.Add(result);
        capture.Click += (_, _) => CaptureScreen(); remove.Click += (_, _) => ClearImage();
        send.Click += async (_, _) => await Send(); refresh.Click += async (_, _) => await List();
        reply.Click += async (_, _) => await Reply(); ticketPicker.SelectedIndexChanged += TicketPickerChanged;
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
            using var pipe = await LocalStatusPipe.OpenVerifiedAsync(Process.GetCurrentProcess().SessionId, timeout.Token);
            await TrayProtocol.Write(pipe,request,timeout.Token,TrayProtocol.MaxSupportPayload);
            var response = await TrayProtocol.Read<TraySupportResponse>(pipe,timeout.Token,TrayProtocol.MaxSupportPayload);
            if (response.Version != TrayProtocol.Version || response.RequestId != request.RequestId) throw new InvalidDataException();
            return response;
        },timeout.Token);
    }
    private async Task Work(Func<Task> action)
    {
        if (busy) return; busy = true;
        send.Enabled = refresh.Enabled = capture.Enabled = remove.Enabled = subject.Enabled = description.Enabled = category.Enabled = consent.Enabled = reply.Enabled = ticketPicker.Enabled = false;
        try { await action(); }
        catch (Exception e) when (!stop.IsCancellationRequested)
        {
            result.Text = e switch {
                OperationCanceledException => "Il servizio non ha risposto in tempo. Premi Aggiorna ticket per riprovare.",
                UnauthorizedAccessException => "Connessione al servizio non verificata. Contatta l'assistenza IT (IPC identita).",
                System.ComponentModel.Win32Exception w => $"Connessione locale al servizio non riuscita (IPC Windows {w.NativeErrorCode}).",
                System.Text.Json.JsonException => "Risposta ticket non compatibile. Contatta l'assistenza IT (IPC formato).",
                InvalidDataException => "Risposta ticket non valida. Contatta l'assistenza IT (IPC dati).",
                IOException => "Connessione locale interrotta. Premi Aggiorna ticket (IPC interrotta).",
                _ => "Impossibile leggere i ticket. Contatta l'assistenza IT (" + e.GetType().Name + ")."
            };
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
        finally
        {
            busy = false;
            if (!IsDisposed) { send.Enabled = refresh.Enabled = capture.Enabled = remove.Enabled = subject.Enabled = description.Enabled = category.Enabled = consent.Enabled = ticketPicker.Enabled = true; reply.Enabled = conversationCanReply; }
        }
    }
    private Task List() => Work(async () => {
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.ListTicketsMethod));
        if (stop.IsCancellationRequested) return;
        result.Text = response.Error ?? "Ticket aggiornati";
        if (response.Error is null)
        {
            var previousId = selectedTicket?.Id;
            ticketPicker.SelectedIndexChanged -= TicketPickerChanged;
            ticketPicker.Items.Clear();
            foreach (var item in response.Tickets) ticketPicker.Items.Add(item);
            selectedTicket = response.Tickets.FirstOrDefault(t => t.Id == previousId) ?? response.Tickets.FirstOrDefault();
            if (selectedTicket is not null) ticketPicker.SelectedItem = selectedTicket;
            ticketPicker.SelectedIndexChanged += TicketPickerChanged;
            await LoadConversationCore();
        }
    });
    private async void TicketPickerChanged(object? sender, EventArgs e) => await LoadConversation();
    private Task LoadConversation() => Work(LoadConversationCore);
    private async Task LoadConversationCore()
    {
        selectedTicket = ticketPicker.SelectedItem as SupportTicketView;
        if (selectedTicket is null) { conversation.Clear(); conversationCanReply = false; return; }
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.TicketConversationMethod,TicketId:selectedTicket.Id));
        if (response.Error is not null) { result.Text = response.Error; return; }
        var thread = response.Conversation;
        if (thread is null) { result.Text = "Conversazione non disponibile."; return; }
        selectedTicket = selectedTicket with { Version = thread.Version, State = thread.State };
        conversationCanReply = thread.State is "open" or "in_progress";
        conversation.Text = string.Join(Environment.NewLine + Environment.NewLine, thread.Messages.Select(m =>
            $"{(m.Kind == "request" ? "Richiesta" : m.Kind == "requester_reply" ? "Tu" : m.Kind == "state_change" ? "Stato" : "Team IT")} · {m.CreatedAt.ToLocalTime():g}\r\n" +
            (m.Kind == "state_change" ? $"{m.FromState} → {m.ToState}" : m.Body) +
            (m.Attachments is { Length: > 0 } ? "\r\n" + string.Join("\r\n", m.Attachments.Select(AttachmentLabel)) : "")));
        if (thread.State is "resolved" or "closed") result.Text = "Ticket chiuso: apri una nuova richiesta per continuare.";
    }
    private static string AttachmentLabel(SupportRequesterAttachmentView attachment) => attachment.ScanState switch
    {
        "clean" => $"📎 {attachment.FileName} · {FormatSize(attachment.SizeBytes)} · pronto nel portale OmniDesk",
        "infected" => "📎 Allegato bloccato dalla scansione antivirus",
        "scan_failed" => "📎 Scansione allegato non riuscita; contatta l’assistenza IT",
        _ => "📎 Allegato in quarantena o scansione in corso"
    };
    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / (1024.0 * 1024):0.0} MB";
    private Task Reply() => Work(async () => {
        if (selectedTicket is null || string.IsNullOrWhiteSpace(replyText.Text)) { result.Text = "Seleziona un ticket aperto e scrivi una risposta."; return; }
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.ReplyToTicketMethod,
            TicketId:selectedTicket.Id, ExpectedVersion:selectedTicket.Version, Body:replyText.Text));
        if (response.Error is not null) { result.Text = response.Error; return; }
        replyText.Clear(); result.Text = "Risposta inviata al team IT.";
        await LoadConversationCore();
    });
    private Task Send() => Work(async () => {
        if (string.IsNullOrWhiteSpace(subject.Text) || string.IsNullOrWhiteSpace(description.Text)) { result.Text = "Compila oggetto e descrizione."; return; }
        if (screenshot is not null && !consent.Checked) { result.Text = "Per allegare lo screenshot occorre il consenso; puoi anche rimuoverlo."; return; }
        var image = screenshot is null ? null : Convert.ToBase64String(screenshot);
        if (pending is null || pending.Subject != subject.Text || pending.Description != description.Text || pending.ScreenshotBase64 != image)
            pending = new(Guid.NewGuid(),subject.Text,description.Text,image,consent.Checked, category.SelectedIndex switch { 1 => "hardware", 2 => "software", 3 => "network", 4 => "account", 5 => "other", _ => "general" });
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.CreateTicketMethod,pending));
        if (stop.IsCancellationRequested) return;
        result.Text = response.Error ?? $"Richiesta registrata: OD-{response.Tickets[0].Number:000000}";
        if (response.Error is null) { pending = null; subject.Clear(); description.Clear(); ClearImage(); }
    });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); preview.Image?.Dispose(); stop.Dispose(); }
        base.Dispose(disposing);
    }
}
