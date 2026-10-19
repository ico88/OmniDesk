using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using OmniDesk.Contracts;
using System.Security.Principal;
using OmniDesk.Windows.Ipc;

namespace OmniDesk.Agent.Windows;

public sealed class TrayPipeWorker(AgentRuntimeState state, ILogger<TrayPipeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new Dictionary<int, (InteractiveLogon Logon, CancellationTokenSource Stop, Task Task)>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<InteractiveLogon> logons;
                try { logons = InteractiveLogons.Read(); }
                catch (Exception e) {
                    logger.LogWarning("Interactive logon enumeration unavailable: {Type}", e.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue;
                }
                foreach (var id in running.Keys.ToArray())
                    if (!logons.Contains(running[id].Logon) || running[id].Task.IsCompleted)
                    {
                        var old = running[id]; old.Stop.Cancel(); await old.Task; old.Stop.Dispose(); running.Remove(id);
                    }
                foreach (var logon in logons)
                    if (!running.ContainsKey(logon.SessionId))
                    {
                        var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        running.Add(logon.SessionId, (logon, stop, Serve(logon, stop.Token)));
                    }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var item in running.Values) item.Stop.Cancel();
            await Task.WhenAll(running.Values.Select(x => x.Task));
            foreach (var item in running.Values) item.Stop.Dispose();
        }
    }
    private static async Task<TraySupportResponse> Support(TrayRequest request, InteractiveLogon logon, CancellationToken ct)
    {
        try
        {
            var identity = AgentIdentity.Load();
            if (identity is null) return new(TrayProtocol.Version, request.RequestId, "Dispositivo non registrato.", []);
            // Identity comes exclusively from the authenticated pipe logon, never a client payload.
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.DeviceId + ":" + logon.UserSid)));
            using var client = AgentConnection.CreateClient();
            var basePath = "api/agent/support/" + key;
            var ticketPath = request.TicketId is null ? "" : "/" + request.TicketId.Value.ToString("D") + "/messages";
            var isPost = request.Method is TrayProtocol.CreateTicketMethod or TrayProtocol.ReplyToTicketMethod;
            var path = request.Method == TrayProtocol.CreateTicketMethod ? "api/agent/support" :
                request.Method == TrayProtocol.ListTicketsMethod ? basePath : basePath + ticketPath;
            using var message = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get,
                new Uri(AgentIdentity.ValidateServer(identity.ServerUrl), path));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Assertion());
            if (request.Method == TrayProtocol.CreateTicketMethod) message.Content = JsonContent.Create(new SupportIntake(key, request.Ticket!));
            else if (request.Method == TrayProtocol.ReplyToTicketMethod)
                message.Content = JsonContent.Create(new SupportRequesterReply(key, request.ExpectedVersion!.Value, request.Body!));
            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return new(TrayProtocol.Version, request.RequestId, $"Richiesta non accettata (HTTP {(int)response.StatusCode}).", []);
            if (request.Method == TrayProtocol.TicketConversationMethod)
                return new(TrayProtocol.Version, request.RequestId, null, [], await response.Content.ReadFromJsonAsync<SupportRequesterConversation>(ct));
            if (request.Method == TrayProtocol.ReplyToTicketMethod)
                return new(TrayProtocol.Version, request.RequestId, null, []);
            var tickets = request.Method == TrayProtocol.CreateTicketMethod
                ? new[] { (await response.Content.ReadFromJsonAsync<SupportTicketView>(ct))! }
                : (await response.Content.ReadFromJsonAsync<SupportTicketView[]>(ct))!;
            return new(TrayProtocol.Version, request.RequestId, null, tickets);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        { return new(TrayProtocol.Version, request.RequestId, "Server non raggiungibile. Puoi ritentare la stessa richiesta.", []); }
    }
    private async Task Serve(InteractiveLogon logon, CancellationToken ct)
    {
        try
        {
            using var pipe = LocalStatusPipe.Create(logon.SessionId, new SecurityIdentifier(logon.LogonSid));
            while (!ct.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var request = await TrayProtocol.Read<TrayRequest>(pipe, timeout.Token, TrayProtocol.MaxSupportPayload);
                    if (!TrayProtocol.Valid(request)) throw new InvalidDataException("IPC request rejected.");
                    var allowed = false;
                    pipe.RunAsClient(() => {
                        using var client = WindowsIdentity.GetCurrent(true);
                        allowed = client is not null && client.User?.Value == logon.UserSid && TokenLogonSid.Read(client.AccessToken).Value == logon.LogonSid;
                    });
                    if (!allowed || !InteractiveLogons.Read().Contains(logon)) throw new UnauthorizedAccessException("IPC logon no longer authorized.");
                    if (request.Method == TrayProtocol.StatusMethod)
                        await TrayProtocol.Write(pipe, state.Snapshot(request.RequestId), timeout.Token);
                    else
                        await TrayProtocol.Write(pipe, await Support(request, logon, timeout.Token), timeout.Token, TrayProtocol.MaxSupportPayload);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException)
                { if (!ct.IsCancellationRequested) logger.LogWarning("Tray IPC request denied/failed in session {SessionId}: {Type}", logon.SessionId, e.GetType().Name); }
                finally { if (pipe.IsConnected) pipe.Disconnect(); }
                await Task.Delay(1000, ct); // bounded one request/connection and one connection/second per logon
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { logger.LogWarning("Tray IPC unavailable in session {SessionId}: {Type}", logon.SessionId, e.GetType().Name); }
    }
}
