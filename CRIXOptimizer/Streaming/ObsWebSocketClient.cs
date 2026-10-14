// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Streaming
{
    /// <summary>Immutable snapshot of what OBS reported.</summary>
    public sealed class ObsStatus
    {
        public static readonly ObsStatus Disconnected = new ObsStatus();

        public bool Connected;
        public bool Streaming;
        public bool Recording;
        /// <summary>Replay buffer or virtual camera running: OBS is capturing the game without recording or streaming.</summary>
        public bool ReplayBuffer;
        public bool VirtualCam;
        public bool AuthenticationRequired;
        public bool AuthenticationFailed;
        /// <summary>The user disconnected CRIX from OBS's WebSocket settings (close code 4011); CRIX does not reconnect.</summary>
        public bool Kicked;
        public string ObsVersion = "";
        public string LastError = "";

        public bool LiveOrRecording => Connected && (Streaming || Recording);
        public bool OtherOutput => ReplayBuffer || VirtualCam;

        public ObsStatus With(Action<ObsStatus> change)
        {
            var copy = (ObsStatus)MemberwiseClone();
            change(copy);
            return copy;
        }
    }

    /// <summary>
    /// Read-only obs-websocket v5 client (built into OBS Studio 28+). It connects to 127.0.0.1 only,
    /// identifies (with the user's password if OBS requires one), subscribes to output events and asks
    /// whether OBS is streaming, recording, or running its replay buffer or virtual camera (GetStreamStatus,
    /// GetRecordStatus, GetReplayBufferStatus, GetVirtualCamStatus). It never sends any other request: CRIX cannot start, stop or
    /// change anything in OBS. Runs on its own background thread; the game thread only reads <see cref="Status"/>.
    ///
    /// The WebSocket framing (RFC 6455) is implemented here on a plain TcpClient so it behaves the same on
    /// every Mono version (no dependency on ClientWebSocket support in Unity's runtime).
    /// </summary>
    public sealed class ObsWebSocketClient : IDisposable
    {
        // The only request types this client is allowed to send.
        public const string GetStreamStatus = "GetStreamStatus";
        public const string GetRecordStatus = "GetRecordStatus";
        public const string GetReplayBufferStatus = "GetReplayBufferStatus";
        public const string GetVirtualCamStatus = "GetVirtualCamStatus";
        private const int EventSubscriptionOutputs = 1 << 6;
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly string host;
        private readonly int port;
        private readonly string password;
        private readonly ILogSink log;
        private readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        private readonly AutoResetEvent wakeSignal = new AutoResetEvent(false);
        private Thread thread;
        private const int MaxMessageBytes = 4 * 1024 * 1024;
        /// <summary>Returned by ReadMessage after a control frame (ping/pong) so the caller can go back to polling.</summary>
        private static readonly Dictionary<string, object> ControlFrame = new Dictionary<string, object>();
        private MemoryStream partialMessage;
        private bool authWarningLogged;
        private volatile ObsStatus status = ObsStatus.Disconnected;
        private volatile bool stopping;

        public ObsWebSocketClient(int port, string password, ILogSink log, string host = "127.0.0.1")
        {
            // Localhost only: CRIX never talks to other machines.
            if (host != "127.0.0.1" && host != "localhost" && host != "::1")
                throw new ArgumentException("OBS status client only connects to this computer", nameof(host));
            this.host = host;
            this.port = port;
            this.password = password ?? "";
            this.log = log ?? NullLogSink.Instance;
        }

        public ObsStatus Status => status;

        /// <summary>Seconds between status polls while connected (events usually arrive sooner).</summary>
        public int PollSeconds { get; set; } = 10;

        /// <summary>Seconds to wait before reconnecting; doubles up to MaxRetrySeconds while OBS is unreachable.</summary>
        public int RetrySeconds { get; set; } = 5;
        public int MaxRetrySeconds { get; set; } = 60;

        public void Start()
        {
            if (thread != null)
                return;
            thread = new Thread(Run) { IsBackground = true, Name = "CRIX OBS status" };
            thread.Start();
        }

        /// <summary>Retry now instead of waiting for the back-off (e.g. OBS has just been started). Safe from any thread.</summary>
        public void Nudge()
        {
            if (!status.Connected && !status.Kicked)
                wakeSignal.Set();
        }

        /// <summary>Asks the background thread to stop without waiting for it (safe to call from the game thread).</summary>
        public void Stop()
        {
            stopping = true;
            stopSignal.Set();
        }

        public void Dispose()
        {
            Stop();
            try
            {
                thread?.Join(2000);
            }
            catch
            {
                // background thread; the process can exit without it
            }
            thread = null;
        }

        private void Run()
        {
            int retry = RetrySeconds;
            bool wasConnected = false;
            string lastLoggedError = null;
            while (!stopping)
            {
                bool hadSession = false;
                try
                {
                    RunSession(ref wasConnected);
                    retry = RetrySeconds;
                }
                catch (Exception e)
                {
                    hadSession = wasConnected;
                    string message = Describe(e);
                    status = status.With(s =>
                    {
                        s.Connected = false;
                        s.Streaming = false;
                        s.Recording = false;
                        s.LastError = message;
                    });
                    if (wasConnected)
                    {
                        log.Info("OBS WebSocket disconnected (" + message + ").");
                        wasConnected = false;
                    }
                    else if (message != lastLoggedError)
                    {
                        log.Debug("OBS WebSocket not reachable: " + message);
                    }
                    lastLoggedError = message;
                }

                if (stopping)
                    break;
                // After a working session ends (OBS closed or restarted), start again from the short delay.
                if (hadSession)
                    retry = RetrySeconds;
                // A wrong or missing password will not fix itself quickly; check again once a minute.
                int wait = status.AuthenticationFailed || (status.AuthenticationRequired && password.Length == 0) ? 60 : retry;
                int signalled = WaitHandle.WaitAny(new WaitHandle[] { stopSignal, wakeSignal }, TimeSpan.FromSeconds(wait));
                if (signalled == 0 || stopping)
                    break;
                retry = signalled == 1 ? RetrySeconds : Math.Min(MaxRetrySeconds, retry * 2);
            }
            // Keep the "kicked" state visible: OBS asked this client not to reconnect.
            if (!status.Kicked)
                status = ObsStatus.Disconnected;
        }

        private void RunSession(ref bool wasConnected)
        {
            using (var tcp = new TcpClient())
            {
                IAsyncResult connect = tcp.BeginConnect(host, port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)) || !tcp.Connected)
                    throw new IOException("connection refused or timed out (is OBS running with its WebSocket server enabled?)");
                tcp.EndConnect(connect);
                tcp.NoDelay = true;
                NetworkStream stream = tcp.GetStream();
                partialMessage = null;
                stream.ReadTimeout = 10000;
                stream.WriteTimeout = 5000;

                Handshake(stream);

                // Hello
                Dictionary<string, object> hello = ReadDataMessage(stream, tcp);
                if (hello == null || MiniJson.Num(hello, "op") != 0)
                    throw new InvalidDataException("expected obs-websocket Hello");
                Dictionary<string, object> helloData = MiniJson.Obj(hello, "d");
                Dictionary<string, object> auth = MiniJson.Obj(helloData, "authentication");
                string version = MiniJson.Str(helloData, "obsWebSocketVersion") ?? "";

                var identify = new Dictionary<string, object>
                {
                    { "rpcVersion", 1 },
                    { "eventSubscriptions", EventSubscriptionOutputs },
                };
                if (auth != null)
                {
                    if (password.Length == 0)
                    {
                        status = status.With(s => { s.AuthenticationRequired = true; s.LastError = "OBS requires a password"; });
                        if (!authWarningLogged)
                            log.Warn("OBS WebSocket requires a password. Copy it from OBS (Tools > WebSocket Server Settings > Show Connect Info) into " +
                                     "[Streaming] ObsWebSocketPassword in com.crix.crixoptimizer.cfg, or turn authentication off in OBS.");
                        authWarningLogged = true;
                        return;
                    }
                    identify["authentication"] = ComputeAuthentication(password, MiniJson.Str(auth, "salt") ?? "", MiniJson.Str(auth, "challenge") ?? "");
                }
                Send(stream, new Dictionary<string, object> { { "op", 1 }, { "d", identify } });

                Dictionary<string, object> identified = ReadDataMessage(stream, tcp);
                if (identified == null)
                {
                    if (lastCloseCode == 4009)
                    {
                        status = status.With(s => { s.AuthenticationFailed = true; s.LastError = "OBS rejected the password"; });
                        if (!authWarningLogged)
                            log.Warn("OBS WebSocket rejected the password in [Streaming] ObsWebSocketPassword.");
                        authWarningLogged = true;
                        return;
                    }
                    throw new IOException("OBS closed the connection during identification (code " + lastCloseCode + ")");
                }
                if (MiniJson.Num(identified, "op") != 2)
                    throw new InvalidDataException("expected obs-websocket Identified");

                status = new ObsStatus { Connected = true, ObsVersion = version };
                if (!wasConnected)
                    log.Info("Connected to OBS WebSocket " + version + " (read-only: streaming/recording state).");
                wasConnected = true;

                RequestStatus(stream);
                DateTime nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds);
                while (!stopping)
                {
                    // Wait for data in short slices so Dispose and polling stay responsive.
                    if (!tcp.Client.Poll(500_000, SelectMode.SelectRead))
                    {
                        if (DateTime.UtcNow >= nextPoll)
                        {
                            RequestStatus(stream);
                            nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds);
                        }
                        continue;
                    }
                    Dictionary<string, object> message = ReadMessage(stream, tcp);
                    if (message == null)
                    {
                        if (lastCloseCode == 4011)
                        {
                            // The user kicked this client in OBS; the protocol says not to reconnect.
                            status = status.With(s => { s.Connected = false; s.Streaming = false; s.Recording = false; s.Kicked = true; s.LastError = "disconnected by the user in OBS"; });
                            log.Info("OBS WebSocket: disconnected from OBS's settings; CRIX will not reconnect until the game restarts or the setting changes.");
                            stopping = true;
                            return;
                        }
                        throw new IOException("OBS closed the connection (code " + lastCloseCode + ")");
                    }
                    if (!ReferenceEquals(message, ControlFrame))
                        Handle(message);
                }
            }
        }

        private void RequestStatus(Stream stream)
        {
            SendRequest(stream, GetStreamStatus, "crix-stream");
            SendRequest(stream, GetRecordStatus, "crix-record");
            SendRequest(stream, GetReplayBufferStatus, "crix-replay");
            SendRequest(stream, GetVirtualCamStatus, "crix-vcam");
        }

        private void SendRequest(Stream stream, string requestType, string requestId)
        {
            if (requestType != GetStreamStatus && requestType != GetRecordStatus && requestType != GetReplayBufferStatus && requestType != GetVirtualCamStatus)
                throw new InvalidOperationException("CRIX only reads OBS status");
            Send(stream, new Dictionary<string, object>
            {
                { "op", 6 },
                { "d", new Dictionary<string, object> { { "requestType", requestType }, { "requestId", requestId } } },
            });
        }

        /// <summary>Applies a response or event to the status snapshot. Internal for tests.</summary>
        internal void Handle(Dictionary<string, object> message)
        {
            double? op = MiniJson.Num(message, "op");
            Dictionary<string, object> d = MiniJson.Obj(message, "d");
            if (op == 7)
            {
                string type = MiniJson.Str(d, "requestType");
                bool? active = MiniJson.Bool(MiniJson.Obj(d, "responseData"), "outputActive");
                if (!active.HasValue)
                    return;
                if (type == GetStreamStatus)
                    SetOutput(stream: active.Value, record: null);
                else if (type == GetRecordStatus)
                    SetOutput(stream: null, record: active.Value);
                else if (type == GetReplayBufferStatus)
                    SetOther(replay: active.Value, vcam: null);
                else if (type == GetVirtualCamStatus)
                    SetOther(replay: null, vcam: active.Value);
            }
            else if (op == 5)
            {
                string type = MiniJson.Str(d, "eventType");
                bool? active = MiniJson.Bool(MiniJson.Obj(d, "eventData"), "outputActive");
                if (!active.HasValue)
                    return;
                if (type == "StreamStateChanged")
                    SetOutput(stream: active.Value, record: null);
                else if (type == "RecordStateChanged")
                    SetOutput(stream: null, record: active.Value);
                else if (type == "ReplayBufferStateChanged")
                    SetOther(replay: active.Value, vcam: null);
                else if (type == "VirtualcamStateChanged")
                    SetOther(replay: null, vcam: active.Value);
            }
        }

        private void SetOther(bool? replay, bool? vcam)
        {
            status = status.With(s =>
            {
                if (replay.HasValue) s.ReplayBuffer = replay.Value;
                if (vcam.HasValue) s.VirtualCam = vcam.Value;
            });
        }

        private void SetOutput(bool? stream, bool? record)
        {
            ObsStatus before = status;
            ObsStatus after = before.With(s =>
            {
                if (stream.HasValue) s.Streaming = stream.Value;
                if (record.HasValue) s.Recording = record.Value;
            });
            status = after;
            if (before.Streaming != after.Streaming)
                log.Info(after.Streaming ? "OBS reports: live stream started." : "OBS reports: live stream stopped.");
            if (before.Recording != after.Recording)
                log.Info(after.Recording ? "OBS reports: recording started." : "OBS reports: recording stopped.");
        }

        /// <summary>obs-websocket v5 authentication string: base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
        public static string ComputeAuthentication(string password, string salt, string challenge)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        // ------------------------------------------------------------------ WebSocket (RFC 6455) client

        private int lastCloseCode;

        private void Handshake(NetworkStream stream)
        {
            var keyBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(keyBytes);
            string key = Convert.ToBase64String(keyBytes);
            string request =
                "GET / HTTP/1.1\r\n" +
                $"Host: {host}:{port}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {key}\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Protocol: obswebsocket.json\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);

            string response = ReadHttpHeaders(stream);
            string firstLine = response.Split('\n')[0];
            if (firstLine.IndexOf(" 101", StringComparison.Ordinal) < 0)
                throw new IOException("WebSocket upgrade refused: " + firstLine.Trim());

            string expected;
            using (SHA1 sha1 = SHA1.Create())
                expected = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            if (response.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new IOException("WebSocket handshake failed (bad Sec-WebSocket-Accept)");
        }

        private static string ReadHttpHeaders(Stream stream)
        {
            var sb = new StringBuilder(256);
            while (sb.Length < 8192)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("connection closed during handshake");
                sb.Append((char)b);
                int n = sb.Length;
                if (n >= 4 && sb[n - 4] == '\r' && sb[n - 3] == '\n' && sb[n - 2] == '\r' && sb[n - 1] == '\n')
                    return sb.ToString();
            }
            throw new IOException("handshake response too large");
        }

        /// <summary>Like ReadMessage, but skips control frames (used during the handshake).</summary>
        private Dictionary<string, object> ReadDataMessage(NetworkStream stream, TcpClient tcp)
        {
            while (true)
            {
                Dictionary<string, object> message = ReadMessage(stream, tcp);
                if (!ReferenceEquals(message, ControlFrame))
                    return message;
            }
        }

        /// <summary>
        /// Reads frames until one complete text message (parsed as JSON) or one control frame has been handled.
        /// Returns null when the server closed the connection and <see cref="ControlFrame"/> after a ping/pong, so
        /// the caller never blocks waiting for a message that may not come. Fragments of a message are kept across
        /// calls, because control frames may arrive between them (RFC 6455 section 5.4).
        /// </summary>
        private Dictionary<string, object> ReadMessage(NetworkStream stream, TcpClient tcp)
        {
            while (true)
            {
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                if (b0 < 0 || b1 < 0)
                    return null;
                bool fin = (b0 & 0x80) != 0;
                int opcode = b0 & 0x0F;
                bool masked = (b1 & 0x80) != 0;
                long length = b1 & 0x7F;
                if (length == 126)
                {
                    byte[] ext = ReadExactly(stream, 2);
                    length = (ext[0] << 8) | ext[1];
                }
                else if (length == 127)
                {
                    byte[] ext = ReadExactly(stream, 8);
                    length = 0;
                    for (int i = 0; i < 8; i++)
                        length = (length << 8) | ext[i];
                }
                long buffered = partialMessage?.Length ?? 0;
                if (length > MaxMessageBytes || buffered + length > MaxMessageBytes)
                    throw new InvalidDataException("WebSocket message too large");
                byte[] mask = masked ? ReadExactly(stream, 4) : null;
                byte[] data = ReadExactly(stream, (int)length);
                if (mask != null)
                    for (int i = 0; i < data.Length; i++)
                        data[i] ^= mask[i & 3];

                switch (opcode)
                {
                    case 0x8: // close
                        lastCloseCode = data.Length >= 2 ? (data[0] << 8) | data[1] : 1005;
                        try { SendFrame(stream, 0x8, data.Length >= 2 ? new[] { data[0], data[1] } : new byte[0]); } catch { /* closing anyway */ }
                        return null;
                    case 0x9: // ping
                        SendFrame(stream, 0xA, data);
                        return ControlFrame;
                    case 0xA: // pong
                        return ControlFrame;
                    case 0x1: // text
                    case 0x0: // continuation
                        if (partialMessage == null)
                            partialMessage = new MemoryStream();
                        partialMessage.Write(data, 0, data.Length);
                        if (!fin)
                            continue;
                        string text = Encoding.UTF8.GetString(partialMessage.ToArray());
                        partialMessage = null;
                        return MiniJson.Parse(text) as Dictionary<string, object> ?? ControlFrame;
                    default:
                        return ControlFrame; // binary frames are not used by the JSON protocol
                }
            }
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("connection closed mid-frame");
                offset += read;
            }
            return buffer;
        }

        private static void Send(Stream stream, Dictionary<string, object> message) =>
            SendFrame(stream, 0x1, Encoding.UTF8.GetBytes(MiniJson.Serialize(message)));

        private static void SendFrame(Stream stream, int opcode, byte[] data)
        {
            // Client-to-server frames must be masked (RFC 6455 section 5.3).
            var header = new List<byte>(14) { (byte)(0x80 | opcode) };
            if (data.Length < 126)
            {
                header.Add((byte)(0x80 | data.Length));
            }
            else if (data.Length <= ushort.MaxValue)
            {
                header.Add(0x80 | 126);
                header.Add((byte)(data.Length >> 8));
                header.Add((byte)data.Length);
            }
            else
            {
                header.Add(0x80 | 127);
                long len = data.Length;
                for (int i = 7; i >= 0; i--)
                    header.Add((byte)(len >> (8 * i)));
            }
            var mask = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(mask);
            header.AddRange(mask);
            var frame = new byte[header.Count + data.Length];
            header.CopyTo(frame, 0);
            for (int i = 0; i < data.Length; i++)
                frame[header.Count + i] = (byte)(data[i] ^ mask[i & 3]);
            stream.Write(frame, 0, frame.Length);
        }

        private static string Describe(Exception e)
        {
            Exception inner = e;
            while (inner.InnerException != null)
                inner = inner.InnerException;
            return inner is SocketException se ? "socket " + se.SocketErrorCode : inner.Message;
        }
    }
}
