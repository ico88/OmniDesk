using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using OmniDesk.Contracts;
using OmniDesk.Windows.Ipc;

namespace OmniDesk.Tray.Windows;

internal sealed class SupportForm : Form
{
    private readonly TextBox subject = new() { MaxLength = 200, Dock = DockStyle.Fill, PlaceholderText = "Riassumi il problema in poche parole" };
    private readonly ComboBox category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox description = new() { MaxLength = 8000, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Che cosa stavi facendo? Che cosa è successo?" };
    private readonly CheckBox consent = new() { Text = "Acconsento ad allegare lo screenshot al team IT", AutoSize = true, Dock = DockStyle.Fill };
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label previewPlaceholder = new() { Dock = DockStyle.Fill, Text = "Anteprima dello screenshot\r\nNon verrà inviato senza il tuo consenso.", TextAlign = ContentAlignment.MiddleCenter };
    private readonly ComboBox ticketPicker = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox conversation = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly ComboBox attachmentPicker = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button downloadAttachment = new() { Text = "Scarica allegato", AutoSize = true, Enabled = false };
    private readonly TextBox replyText = new() { MaxLength = 8000, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Scrivi un messaggio al team IT" };
    private readonly Label result = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(22, 0, 22, 0), TextAlign = ContentAlignment.MiddleLeft, Text = "Puoi inviare una richiesta o controllare i tuoi ticket." };
    private readonly TabControl pages = new() { Dock = DockStyle.Fill };
    private readonly Label ticketSummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button send = new() { Text = "Invia richiesta al team IT", AutoSize = true };
    private readonly Button capture = new() { Text = "Acquisisci screenshot", AutoSize = true };
    private readonly Button remove = new() { Text = "Rimuovi screenshot", AutoSize = true };
    private readonly Button refresh = new() { Text = "Aggiorna elenco", AutoSize = true };
    private readonly Button reply = new() { Text = "Invia messaggio", AutoSize = true, Enabled = false };
    private SupportTicketView? selectedTicket;
    private bool conversationCanReply;
    private bool attachmentDownloadAvailable;
    private byte[]? screenshot;
    private SupportCreate? pending;
    private bool busy;
    private readonly CancellationTokenSource stop = new();

    public SupportForm()
    {
        Text = "OmniDesk — Assistenza"; Width = 850; Height = 790; MinimumSize = new Size(720, 700); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 247, 251); Font = new Font("Segoe UI", 9F); ForeColor = Color.FromArgb(33, 48, 69);
        var requestPage = new TabPage("Nuova richiesta") { BackColor = BackColor, Padding = new Padding(0) };
        var ticketsPage = new TabPage("I miei ticket") { BackColor = BackColor, Padding = new Padding(0) };
        pages.TabPages.AddRange([requestPage, ticketsPage]);

        var requestLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22, 18, 22, 18), BackColor = BackColor };
        requestLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var requestHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 0, 0, 12) };
        var requestTitle = new Label { Text = "Come possiamo aiutarti?", Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 17F), ForeColor = Color.FromArgb(25, 45, 70) };
        var requestSubheading = new Label { Text = "La richiesta sarà collegata a questo computer e inviata al team IT.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(91, 108, 130), AutoEllipsis = true };
        requestHeading.Controls.Add(requestSubheading); requestHeading.Controls.Add(requestTitle);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = BackColor, Margin = new Padding(0) };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); details.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        var subjectLabel = FieldLabel("Oggetto"); var categoryLabel = FieldLabel("Categoria");
        details.Controls.Add(subjectLabel, 0, 0); details.Controls.Add(categoryLabel, 1, 0);
        details.Controls.Add(subject, 0, 1); details.Controls.Add(category, 1, 1);

        var screenshotBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 0) };
        var screenshotLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.White };
        screenshotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); screenshotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        screenshotLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); screenshotLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var screenshotTitle = new Label { Text = "Screenshot  ·  facoltativo", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.FromArgb(25, 45, 70) };
        screenshotLayout.Controls.Add(screenshotTitle, 0, 0); screenshotLayout.SetColumnSpan(screenshotTitle, 2);
        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 248, 252), Margin = new Padding(0, 0, 14, 0) };
        preview.BackColor = Color.FromArgb(245, 248, 252); previewPlaceholder.ForeColor = Color.FromArgb(115, 130, 149);
        previewPanel.Controls.Add(preview); previewPanel.Controls.Add(previewPlaceholder);
        var screenshotActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4, 0, 0, 0) };
        screenshotActions.Controls.Add(capture); screenshotActions.Controls.Add(remove);
        screenshotActions.Controls.Add(new Label { Text = "Puoi controllare l’anteprima prima dell’invio. Lo screenshot viene allegato solo se acquisito e autorizzato qui.", AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.FromArgb(91, 108, 130), Padding = new Padding(2, 3, 2, 5) });
        consent.Text = "Allega lo screenshot alla richiesta"; consent.AutoSize = true; consent.Dock = DockStyle.None;
        screenshotActions.Controls.Add(consent);
        screenshotLayout.Controls.Add(previewPanel, 0, 1); screenshotLayout.Controls.Add(screenshotActions, 1, 1);
        screenshotBox.Controls.Add(screenshotLayout);
        var sendPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0), BackColor = BackColor };
        sendPanel.Controls.Add(send);
        requestLayout.Controls.Add(requestHeading, 0, 0); requestLayout.Controls.Add(details, 0, 1);
        requestLayout.Controls.Add(FieldLabel("Descrizione"), 0, 2); requestLayout.Controls.Add(description, 0, 3);
        requestLayout.Controls.Add(screenshotBox, 0, 4); requestLayout.Controls.Add(sendPanel, 0, 5);
        requestPage.Controls.Add(requestLayout);

        StyleInput(subject); StyleInput(category); StyleInput(description); StyleInput(ticketPicker); StyleInput(replyText);
        StyleButton(send, primary: true); StyleButton(capture); StyleButton(remove); StyleButton(refresh); StyleButton(reply, primary: true); StyleButton(downloadAttachment);
        result.BackColor = Color.FromArgb(235, 241, 248); result.ForeColor = Color.FromArgb(57, 75, 98);

        var ticketLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        ticketLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ticketLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); ticketLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        ticketLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); ticketLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        ticketLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); ticketLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var ticketHeading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        ticketHeading.Controls.Add(new Label { Text = "Seleziona una richiesta", AutoSize = true, Padding = new Padding(0, 7, 5, 0) });
        ticketPicker.Width = 390; ticketHeading.Controls.Add(ticketPicker); ticketHeading.Controls.Add(refresh);
        var attachmentTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        attachmentTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); attachmentTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        attachmentTools.Controls.Add(attachmentPicker, 0, 0); attachmentTools.Controls.Add(downloadAttachment, 1, 0);
        var replyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        replyPanel.Controls.Add(reply);
        ticketLayout.Controls.Add(ticketHeading, 0, 0); ticketLayout.Controls.Add(ticketSummary, 0, 1);
        ticketLayout.Controls.Add(conversation, 0, 2); ticketLayout.Controls.Add(attachmentTools, 0, 3);
        ticketLayout.Controls.Add(replyText, 0, 4); ticketLayout.Controls.Add(replyPanel, 0, 5);
        ticketsPage.Controls.Add(ticketLayout);

        category.Items.AddRange(["Generale", "Hardware", "Software", "Rete", "Account", "Altro"]); category.SelectedIndex = 0;
        Controls.Add(pages); Controls.Add(result);
        capture.Click += (_, _) => CaptureScreen(); remove.Click += (_, _) => ClearImage();
        send.Click += async (_, _) => await Send(); refresh.Click += async (_, _) => await List();
        reply.Click += async (_, _) => await Reply(); downloadAttachment.Click += async (_, _) => await DownloadAttachment(); ticketPicker.SelectedIndexChanged += TicketPickerChanged;
        pages.SelectedIndexChanged += async (_, _) => { if (pages.SelectedTab == ticketsPage) await List(); };
        Shown += async (_, _) => await List();
    }
    private static Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9F), ForeColor = Color.FromArgb(57, 75, 98), Margin = new Padding(0) };
    private static void StyleInput(Control control)
    {
        control.Font = new Font("Segoe UI", 10F); control.BackColor = Color.White; control.ForeColor = Color.FromArgb(33, 48, 69);
        control.Margin = new Padding(0, 0, 12, 3);
        if (control is TextBox text) { text.BorderStyle = BorderStyle.FixedSingle; text.Padding = new Padding(8); }
        if (control is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; }
    }
    private static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1; button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI Semibold", 9F); button.Padding = new Padding(11, 4, 11, 4); button.Height = 36;
        button.BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(47, 66, 90);
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(213, 222, 233);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(242, 246, 251);
    }
    private void ClearImage() { screenshot = null; preview.Image?.Dispose(); preview.Image = null; previewPlaceholder.Visible = true; consent.Checked = false; pending = null; }
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
                if (stream.Length <= 524288) { screenshot = StripMetadata(stream.ToArray()); preview.Image = new Bitmap(resized); previewPlaceholder.Visible = false; break; }
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(request.Method == TrayProtocol.DownloadTicketAttachmentMethod ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(25));
        return await Task.Run(async () => {
            using var pipe = await LocalStatusPipe.OpenVerifiedAsync(Process.GetCurrentProcess().SessionId, timeout.Token);
            await TrayProtocol.Write(pipe,request,timeout.Token,TrayProtocol.MaxSupportPayload);
            var maxPayload = request.Method == TrayProtocol.DownloadTicketAttachmentMethod ? TrayProtocol.MaxAttachmentPayload : TrayProtocol.MaxSupportPayload;
            var response = await TrayProtocol.Read<TraySupportResponse>(pipe,timeout.Token,maxPayload);
            if (response.Version != TrayProtocol.Version || response.RequestId != request.RequestId) throw new InvalidDataException();
            return response;
        },timeout.Token);
    }
    private async Task Work(Func<Task> action)
    {
        if (busy) return; busy = true;
        pages.Enabled = false;
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
            if (!IsDisposed) { pages.Enabled = true; reply.Enabled = conversationCanReply; downloadAttachment.Enabled = attachmentDownloadAvailable; }
        }
    }
    private Task List() => Work(ListCore);
    private async Task ListCore()
    {
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.ListTicketsMethod));
        if (stop.IsCancellationRequested) return;
        result.Text = response.Error ?? (response.Tickets.Length == 0 ? "Non hai ancora richieste. Puoi aprirne una nella scheda Nuova richiesta." : "Elenco richieste aggiornato.");
        if (response.Error is null)
        {
            var previousId = selectedTicket?.Id;
            ticketPicker.SelectedIndexChanged -= TicketPickerChanged;
            ticketPicker.Items.Clear();
            foreach (var item in response.Tickets) ticketPicker.Items.Add(new TrayTicketChoice(item));
            selectedTicket = response.Tickets.FirstOrDefault(t => t.Id == previousId) ?? response.Tickets.FirstOrDefault();
            if (selectedTicket is not null) ticketPicker.SelectedItem = ticketPicker.Items.Cast<TrayTicketChoice>().FirstOrDefault(choice => choice.Ticket.Id == selectedTicket.Id);
            ticketPicker.SelectedIndexChanged += TicketPickerChanged;
            await LoadConversationCore();
        }
    }
    private async void TicketPickerChanged(object? sender, EventArgs e) => await LoadConversation();
    private Task LoadConversation() => Work(LoadConversationCore);
    private async Task LoadConversationCore()
    {
        selectedTicket = (ticketPicker.SelectedItem as TrayTicketChoice)?.Ticket;
        if (selectedTicket is null) { ticketSummary.Text = "Nessun ticket selezionato."; conversation.Clear(); conversationCanReply = attachmentDownloadAvailable = false; attachmentPicker.Items.Clear(); return; }
        ticketSummary.Text = $"OD-{selectedTicket.Number:000000} · {TicketStateLabel(selectedTicket.State)} · aggiornato {selectedTicket.UpdatedAt.ToLocalTime():g}";
        conversation.Clear(); attachmentPicker.Items.Clear(); attachmentDownloadAvailable = false; downloadAttachment.Enabled = false;
        var response = await Call(new(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.TicketConversationMethod,TicketId:selectedTicket.Id));
        if (response.Error is not null) { result.Text = response.Error; return; }
        var thread = response.Conversation;
        if (thread is null) { result.Text = "Conversazione non disponibile."; return; }
        selectedTicket = selectedTicket with { Version = thread.Version, State = thread.State };
        ticketSummary.Text = $"OD-{selectedTicket.Number:000000} · {TicketStateLabel(thread.State)} · aggiornato {selectedTicket.UpdatedAt.ToLocalTime():g}";
        conversationCanReply = thread.State is "open" or "in_progress";
        attachmentPicker.Items.Clear();
        foreach (var item in thread.Messages.Where(message => !message.ContentPurged).SelectMany(message => message.Attachments ?? []).Where(item => item.ScanState == "clean"))
            attachmentPicker.Items.Add(new TrayAttachmentChoice(thread.TicketId, item));
        if (attachmentPicker.Items.Count > 0) attachmentPicker.SelectedIndex = 0;
        attachmentDownloadAvailable = attachmentPicker.Items.Count > 0;
        downloadAttachment.Enabled = attachmentDownloadAvailable && !busy;
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
    private static string TicketStateLabel(string state) => state switch { "open" => "Aperto", "in_progress" => "In lavorazione", "resolved" => "Risolto", "closed" => "Chiuso", _ => state };
    private sealed record TrayTicketChoice(SupportTicketView Ticket)
    {
        public override string ToString() => $"OD-{Ticket.Number:000000} · {TicketStateLabel(Ticket.State)} · {Ticket.Subject}";
    }
    private sealed record TrayAttachmentChoice(Guid TicketId, SupportRequesterAttachmentView Attachment)
    {
        public override string ToString() => $"{Attachment.FileName} · {FormatSize(Attachment.SizeBytes)}";
    }
    private static string SaveDialogName(string name)
    {
        var leaf = name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        var safe = new string(leaf.Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')).ToArray()).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(safe)) return "allegato";
        var stem = safe.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
        return safe.Length <= 255 ? safe : safe[^255..];
    }
    private async Task DownloadAttachment()
    {
        if (attachmentPicker.SelectedItem is not TrayAttachmentChoice choice) { result.Text = "Seleziona un allegato pulito da scaricare."; return; }
        using var dialog = new SaveFileDialog { FileName = SaveDialogName(choice.Attachment.FileName), Filter = "Tutti i file (*.*)|*.*", OverwritePrompt = true, AddExtension = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Work(async () => {
            var response = await Call(new TrayRequest(TrayProtocol.Version, Guid.NewGuid(), TrayProtocol.DownloadTicketAttachmentMethod,
                TicketId: choice.TicketId, AttachmentId: choice.Attachment.Id));
            if (response.Error is not null) { result.Text = response.Error; return; }
            var downloaded = response.DownloadedAttachment;
            if (downloaded is null || downloaded.Id != choice.Attachment.Id || downloaded.SizeBytes is <= 0 or > 10 * 1024 * 1024)
                throw new InvalidDataException("Unexpected attachment response.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(downloaded.ContentBase64); }
            catch (FormatException) { throw new InvalidDataException("Invalid attachment encoding."); }
            if (bytes.LongLength != downloaded.SizeBytes || bytes.LongLength > 10 * 1024 * 1024)
                throw new InvalidDataException("Attachment size mismatch.");
            await File.WriteAllBytesAsync(dialog.FileName, bytes, stop.Token);
            result.Text = $"Allegato salvato: {Path.GetFileName(dialog.FileName)}";
        });
    }
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
        if (response.Error is not null) { result.Text = response.Error; return; }
        selectedTicket = response.Tickets.FirstOrDefault();
        pending = null; subject.Clear(); description.Clear(); ClearImage();
        pages.SelectedIndex = 1;
        await ListCore();
        result.Text = selectedTicket is null
            ? "Richiesta inviata. Puoi seguirne gli aggiornamenti nella scheda I miei ticket."
            : $"Richiesta OD-{selectedTicket.Number:000000} inviata. Gli aggiornamenti appariranno in I miei ticket.";
    });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); preview.Image?.Dispose(); stop.Dispose(); }
        base.Dispose(disposing);
    }
}
