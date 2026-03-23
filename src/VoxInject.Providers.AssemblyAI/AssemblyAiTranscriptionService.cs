using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoxInject.Providers.Abstractions;

namespace VoxInject.Providers.AssemblyAI;

/// <summary>
/// Real-time transcription client for AssemblyAI Streaming API v3.
/// Endpoint : wss://streaming.assemblyai.com/v3/ws
/// Protocol : binary audio frames in, JSON messages out.
/// </summary>
public sealed class AssemblyAiTranscriptionService : ITranscriptionService
{
    private const string WssEndpoint = "wss://streaming.assemblyai.com/v3/ws";

    private ClientWebSocket?         _ws;
    private CancellationTokenSource? _cts;
    private Task?                    _receiveLoop;
    private bool                     _started;
    // ClientWebSocket only allows one SendAsync at a time; drop chunks if busy.
    private readonly SemaphoreSlim   _sendGate = new(1, 1);

    public event Action<string>? PartialTranscript;
    public event Action<string>? FinalTranscript;
    public event Action<string>? SessionError;

    public async Task StartAsync(
        IReadOnlyDictionary<string, string> config,
        string   language,
        bool     autoPunctuation,
        string[] wordBoost)
    {
        if (_started)
            throw new InvalidOperationException("Session already started.");

        if (!config.TryGetValue("ApiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AssemblyAI provider requires an 'ApiKey' config entry.");

        var query = BuildQueryString(language, autoPunctuation, wordBoost);
        var uri   = new Uri($"{WssEndpoint}?{query}");

        _ws  = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        _ws.Options.SetRequestHeader("Authorization", apiKey);

        await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);

        _started     = true;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task SendAudioAsync(byte[] pcm16Chunk)
    {
        if (_ws is null || !_started || _ws.State != WebSocketState.Open) return;

        if (!await _sendGate.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            if (_ws.State != WebSocketState.Open) return;
            await _ws.SendAsync(
                new ArraySegment<byte>(pcm16Chunk),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                _cts!.Token).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_ws is null) return;
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                var terminate = """{"type":"Terminate"}""";
                var bytes     = Encoding.UTF8.GetBytes(terminate);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true,
                    CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SessionError?.Invoke(ex.Message); }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var type = node?["type"]?.GetValue<string>() ?? string.Empty;

            if (type == "Turn")
            {
                var transcript = node?["transcript"]?.GetValue<string>() ?? string.Empty;
                var endOfTurn  = node?["end_of_turn"]?.GetValue<bool>() ?? false;

                if (string.IsNullOrWhiteSpace(transcript)) return;

                if (endOfTurn) FinalTranscript?.Invoke(transcript);
                else           PartialTranscript?.Invoke(transcript);
                return;
            }

            // AssemblyAI may use different type strings and field names for errors
            // depending on the error category (auth, quota, protocol…)
            var isErrorType = type.Equals("Error",               StringComparison.OrdinalIgnoreCase)
                           || type.Equals("AuthenticationError", StringComparison.OrdinalIgnoreCase)
                           || type.Equals("InvalidSession",      StringComparison.OrdinalIgnoreCase)
                           || type.Contains("error",             StringComparison.OrdinalIgnoreCase);

            var errorBody = node?["error"]?.GetValue<string>()
                         ?? node?["message"]?.GetValue<string>();

            if (isErrorType || errorBody is not null)
            {
                SessionError?.Invoke(errorBody ?? $"[{type}] {json}");
                return;
            }

            // SessionBegins, Termination, etc. — informational, no action needed
        }
        catch (JsonException) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildQueryString(string language, bool autoPunctuation, string[] wordBoost)
    {
        var parts = new List<string>
        {
            "encoding=pcm_s16le",
            "sample_rate=16000"
        };

        var model = language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "universal-streaming-english"
            : "universal-streaming-multilingual";

        parts.Add($"speech_model={Uri.EscapeDataString(model)}");

        if (autoPunctuation)
            parts.Add("punctuate=true");

        if (wordBoost.Length > 0)
        {
            var terms = string.Join(",", wordBoost.Select(Uri.EscapeDataString));
            parts.Add($"keyterms_prompt={terms}");
        }

        return string.Join("&", parts);
    }

    private async Task CleanupAsync()
    {
        _started = false;
        _cts?.Cancel();

        if (_receiveLoop != null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch { }
        }

        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done",
                    cts.Token).ConfigureAwait(false);
            }
        }
        catch { }

        _ws?.Dispose();
        _cts?.Dispose();
        _ws          = null;
        _cts         = null;
        _receiveLoop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
