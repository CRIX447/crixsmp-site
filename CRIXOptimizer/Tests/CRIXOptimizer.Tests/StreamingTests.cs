using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CRIXOptimizer.Adaptive;
using CRIXOptimizer.Compatibility;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Core;
using CRIXOptimizer.Streaming;

namespace CRIXOptimizer.Tests
{
    public sealed class MiniJsonTests
    {
        [Test]
        public void ParsesNestedDocuments()
        {
            object v = MiniJson.Parse("{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.5.4\",\"rpcVersion\":1,\"authentication\":{\"challenge\":\"c\",\"salt\":\"s\"},\"list\":[1,2.5,-3e2,true,false,null,\"x\"]}}");
            Assert.Equal(0.0, MiniJson.Num(v, "op").Value);
            Dictionary<string, object> d = MiniJson.Obj(v, "d");
            Assert.Equal("5.5.4", MiniJson.Str(d, "obsWebSocketVersion"));
            Assert.Equal("s", MiniJson.Str(MiniJson.Obj(d, "authentication"), "salt"));
            var list = (List<object>)d["list"];
            Assert.Equal(7, list.Count);
            Assert.Equal(-300.0, (double)list[2]);
            Assert.Equal(true, list[3]);
            Assert.Null(list[5]);
        }

        [Test]
        public void HandlesEscapesAndRoundTrips()
        {
            var original = new Dictionary<string, object>
            {
                { "text", "quote \" backslash \\ newline \n tab \t unicode é \u0001" },
                { "n", 42 },
                { "nested", new Dictionary<string, object> { { "b", false } } },
            };
            string json = MiniJson.Serialize(original);
            var back = (Dictionary<string, object>)MiniJson.Parse(json);
            Assert.Equal((string)original["text"], (string)back["text"]);
            Assert.Equal(42.0, (double)back["n"]);
            Assert.Equal(false, MiniJson.Bool(back["nested"], "b").Value);
            Assert.Equal("é", (string)MiniJson.Parse("\"\\u00e9\""));
        }

        [Test]
        public void RejectsMalformedInput()
        {
            foreach (string bad in new[] { "{", "{\"a\":}", "[1,2", "\"open", "{\"a\" 1}", "tru", "{} extra", new string('[', 100) + new string(']', 100) })
                Assert.False(MiniJson.TryParse(bad, out _), "should reject: " + bad.Substring(0, Math.Min(20, bad.Length)));
        }
    }

    public sealed class CaptureStateTests
    {
        private static CaptureSignals S(CompanionApp apps = CompanionApp.None, GraphicsHook hooks = GraphicsHook.None, bool ws = false, bool live = false, bool rec = false) =>
            new CaptureSignals { Apps = apps, Hooks = hooks, ObsWebSocketConnected = ws, ObsStreaming = live, ObsRecording = rec };

        [Test]
        public void AutoUsesExactObsStateWhenConnected()
        {
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Obs, ws: true), out _), "OBS open but idle");
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Obs, ws: true, rec: true), out string r1));
            Assert.Equal("OBS is recording", r1);
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Obs, ws: true, live: true, rec: true), out string r2));
            Assert.Equal("OBS is live and recording", r2);
        }

        [Test]
        public void AutoFallsBackToRunningAppWithoutWebSocket()
        {
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Obs), out _));
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(hooks: GraphicsHook.ObsGameCapture), out _));
            Assert.Equal(CaptureKind.Discord, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Discord), out _));
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Discord | CompanionApp.StreamlabsDesktop), out _), "OBS outranks Discord");
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(CompanionApp.Spotify | CompanionApp.SteamVR), out _), "Spotify alone is not streaming");
        }

        [Test]
        public void RecordingOrLiveTriggerIsStrict()
        {
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, S(CompanionApp.Obs | CompanionApp.Discord), out _));
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, S(hooks: GraphicsHook.ObsGameCapture), out _));
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, S(hooks: GraphicsHook.ObsGameCapture, ws: true), out _), "connected + idle beats the hook");
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, S(ws: true, live: true), out _));
        }

        [Test]
        public void StaleHooksIgnoredOnceTheirAppHasExited()
        {
            // OBS's capture DLL and Discord's overlay DLL stay loaded in the game after their app closes.
            CaptureSignals gone = S(hooks: GraphicsHook.ObsGameCapture | GraphicsHook.DiscordOverlay);
            gone.ObsHookStale = true;
            gone.DiscordHookStale = true;
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, gone, out _));
            Assert.Equal(CaptureKind.None, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, gone, out _));
            // An app never seen (e.g. OBS run as administrator is invisible to the game) keeps its hook counted.
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.WhenRecordingOrLive, S(hooks: GraphicsHook.ObsGameCapture), out _));
            Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, S(hooks: GraphicsHook.ObsGameCapture), out _));
        }

        [Test]
        public void EscalatesImmediatelyAndHoldsBeforeDroppingBack()
        {
            var m = new CaptureStateMachine { HoldSeconds = 20 };
            Assert.True(m.Update(0, S(CompanionApp.Obs, ws: true, rec: true)));
            Assert.Equal(CaptureKind.Obs, m.Active);
            Assert.False(m.Update(5, S(CompanionApp.Obs, ws: true)), "held");
            Assert.False(m.Update(19, S(CompanionApp.Obs, ws: true)));
            Assert.True(m.Update(26, S(CompanionApp.Obs, ws: true)));
            Assert.Equal(CaptureKind.None, m.Active);
            Assert.True(m.Update(27, S(CompanionApp.Obs, ws: true, live: true)), "back on at once");
        }

        [Test]
        public void SignalReturningDuringHoldKeepsState()
        {
            var m = new CaptureStateMachine { HoldSeconds = 10 };
            m.Update(0, S(CompanionApp.Discord));
            m.Update(5, S());
            m.Update(8, S(CompanionApp.Discord)); // reconnect within the hold
            Assert.False(m.Update(15, S()), "hold restarted at t=8");
            Assert.True(m.Update(19, S()));
        }
    }

    public sealed class ResolutionProbeTests
    {
        [Test]
        public void DetectsIneffectiveResolutionDrop()
        {
            var p = new ResolutionProbe { SettleSeconds = 2, MeasureSeconds = 3 };
            p.Begin(0, 20.0);
            ProbeResult r = ProbeResult.Pending;
            for (double t = 0.5; t <= 6 && r == ProbeResult.Pending; t += 0.5)
                r = p.Sample(t, 19.9); // 0.5% better: CPU-bound
            Assert.Equal(ProbeResult.Ineffective, r);
            Assert.False(p.IsPending);
        }

        [Test]
        public void AcceptsEffectiveDropAndIgnoresSettleSamples()
        {
            var p = new ResolutionProbe { SettleSeconds = 2, MeasureSeconds = 3 };
            p.Begin(0, 22.2);
            ProbeResult r = ProbeResult.Pending;
            for (double t = 0.5; t <= 6 && r == ProbeResult.Pending; t += 0.5)
                r = p.Sample(t, t < 2 ? 40.0 : 11.1); // reallocation hitch during settle is ignored
            Assert.Equal(ProbeResult.Effective, r);
            Assert.True(p.LastImprovement > 0.4);
            Assert.Equal(ProbeResult.Idle, p.Sample(7, 11.1));
        }

        [Test]
        public void HalfRateReprojectionIsInconclusive()
        {
            // 90 Hz budget 11.1 ms; the runtime holds 45 FPS (22.2 ms) before and after: nothing can be concluded.
            var p = new ResolutionProbe { SettleSeconds = 2, MeasureSeconds = 3 };
            p.Begin(0, 22.2, 11.1);
            ProbeResult r = ProbeResult.Pending;
            for (double t = 0.5; t <= 6 && r == ProbeResult.Pending; t += 0.5)
                r = p.Sample(t, 22.1);
            Assert.Equal(ProbeResult.Inconclusive, r);
            // Off the half-rate lock, the same small change is a real "did not help".
            p.Begin(0, 13.0, 11.1);
            r = ProbeResult.Pending;
            for (double t = 0.5; t <= 6 && r == ProbeResult.Pending; t += 0.5)
                r = p.Sample(t, 12.95);
            Assert.Equal(ProbeResult.Ineffective, r);
        }
    }

    public sealed class CpuLeverTests
    {
        [Test]
        public void LadderReducesCosmeticsAndUpdateRatesFirst()
        {
            QualityLevers l1 = QualityLadder.Compute(default, -1, new QualityLadderSettings(), cpuBound: true);
            Assert.Near(0.85, l1.CosmeticScale, 1e-5);
            Assert.Near(0.9, l1.ThrottleScale, 1e-5);
            QualityLevers l10 = QualityLadder.Compute(default, -10, new QualityLadderSettings(), cpuBound: true);
            Assert.Near(0.4, l10.CosmeticScale, 1e-5, "floor");
            Assert.Near(0.5, l10.ThrottleScale, 1e-5, "floor");
            QualityLevers off = QualityLadder.Compute(default, -5, new QualityLadderSettings { AllowCosmetics = false }, false);
            Assert.Equal(1f, off.CosmeticScale);
            Assert.Equal(1f, QualityLadder.Compute(default, 0, new QualityLadderSettings(), false).ThrottleScale);
        }

        [Test]
        public void ApplyCpuLeversScalesProfileWithoutTouchingIt()
        {
            OptimizationSettings profile = ProfilePresets.Balanced();
            profile.TextureTransitionRate = 0f; // user turned this throttle off
            OptimizationSettings s = QualityLadder.ApplyCpuLevers(profile, new QualityLevers { CosmeticScale = 0.5f, ThrottleScale = 0.5f });
            Assert.Near(4.0, s.CosmeticMaximumDistance, 1e-5);
            Assert.Equal(5, s.CosmeticMaximumCount);
            Assert.Equal(2, s.CosmeticFullDetailCount);
            Assert.Near(15.0, s.VoiceVisualRate, 1e-5);
            Assert.Equal(0f, s.TextureTransitionRate, "a disabled throttle stays disabled");
            Assert.Equal(8f, profile.CosmeticMaximumDistance, "original profile unchanged");
            Assert.True(s.CosmeticMaximumDistance >= s.CosmeticFullDetailDistance);
        }
    }

    [Integration]
    public sealed class ObsWebSocketTests
    {
        /// <summary>Minimal obs-websocket v5 server for tests (RFC 6455 server side + protocol).</summary>
        private sealed class FakeObs : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread thread;
            private readonly string password;
            private volatile bool stop;
            private NetworkStream client;
            public readonly List<string> RequestTypes = new List<string>();
            public volatile bool Streaming;
            public volatile bool Recording;
            public volatile bool ReplayBuffer;
            public volatile bool IdentifiedOk;
            public volatile string ReceivedAuth;
            public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
            public const string Salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=";
            public const string Challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=";

            public FakeObs(string password)
            {
                this.password = password;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                thread = new Thread(Serve) { IsBackground = true };
                thread.Start();
            }

            private void Serve()
            {
                try
                {
                    while (!stop)
                    {
                        if (!listener.Pending()) { Thread.Sleep(10); continue; }
                        using (TcpClient tcp = listener.AcceptTcpClient())
                        {
                            client = tcp.GetStream();
                            Handshake(client);
                            var hello = new Dictionary<string, object> { { "obsWebSocketVersion", "5.5.4" }, { "rpcVersion", 1 } };
                            if (password != null)
                                hello["authentication"] = new Dictionary<string, object> { { "challenge", Challenge }, { "salt", Salt } };
                            Send(new Dictionary<string, object> { { "op", 0 }, { "d", hello } });
                            while (!stop)
                            {
                                string text = ReadText(client);
                                if (text == null)
                                    break;
                                if (text.Length == 0)
                                    continue; // pong
                                var msg = (Dictionary<string, object>)MiniJson.Parse(text);
                                Dictionary<string, object> d = MiniJson.Obj(msg, "d");
                                double op = MiniJson.Num(msg, "op").Value;
                                if (op == 1)
                                {
                                    ReceivedAuth = MiniJson.Str(d, "authentication");
                                    if (password != null && ReceivedAuth != ObsWebSocketClient.ComputeAuthentication(password, Salt, Challenge))
                                    {
                                        SendClose(4009);
                                        break;
                                    }
                                    IdentifiedOk = true;
                                    Send(new Dictionary<string, object> { { "op", 2 }, { "d", new Dictionary<string, object> { { "negotiatedRpcVersion", 1 } } } });
                                }
                                else if (op == 6)
                                {
                                    string type = MiniJson.Str(d, "requestType");
                                    lock (RequestTypes) RequestTypes.Add(type);
                                    bool active = type == "GetStreamStatus" ? Streaming : type == "GetRecordStatus" ? Recording
                                        : type == "GetReplayBufferStatus" ? ReplayBuffer : false;
                                    Send(new Dictionary<string, object> { { "op", 7 }, { "d", new Dictionary<string, object> {
                                        { "requestType", type }, { "requestId", MiniJson.Str(d, "requestId") },
                                        { "requestStatus", new Dictionary<string, object> { { "result", true }, { "code", 100 } } },
                                        { "responseData", new Dictionary<string, object> { { "outputActive", active } } } } } });
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // test server: connection ended
                }
            }

            public void RaiseRecordEvent(bool active)
            {
                Recording = active;
                Send(new Dictionary<string, object> { { "op", 5 }, { "d", new Dictionary<string, object> {
                    { "eventType", "RecordStateChanged" }, { "eventIntent", 64 },
                    { "eventData", new Dictionary<string, object> { { "outputActive", active }, { "outputState", active ? "OBS_WEBSOCKET_OUTPUT_STARTED" : "OBS_WEBSOCKET_OUTPUT_STOPPED" } } } } } });
            }

            private static void Handshake(NetworkStream s)
            {
                var sb = new StringBuilder();
                while (!sb.ToString().EndsWith("\r\n\r\n"))
                    sb.Append((char)s.ReadByte());
                string key = sb.ToString().Split('\n').First(l => l.StartsWith("Sec-WebSocket-Key:")).Substring(18).Trim();
                string accept;
                using (SHA1 sha = SHA1.Create())
                    accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                                                          "Sec-WebSocket-Accept: " + accept + "\r\nSec-WebSocket-Protocol: obswebsocket.json\r\n\r\n");
                s.Write(response, 0, response.Length);
            }

            private void Send(Dictionary<string, object> message)
            {
                byte[] data = Encoding.UTF8.GetBytes(MiniJson.Serialize(message));
                var frame = new List<byte> { 0x81 };
                if (data.Length < 126) frame.Add((byte)data.Length);
                else { frame.Add(126); frame.Add((byte)(data.Length >> 8)); frame.Add((byte)data.Length); }
                frame.AddRange(data);
                lock (this) client.Write(frame.ToArray(), 0, frame.Count);
            }

            public void SendClose(int code)
            {
                lock (this) client.Write(new byte[] { 0x88, 2, (byte)(code >> 8), (byte)code }, 0, 4);
            }

            public volatile int PongsReceived;

            public void SendPing()
            {
                lock (this) client.Write(new byte[] { 0x89, 2, (byte)'h', (byte)'i' }, 0, 4);
            }

            /// <summary>Sends a RecordStateChanged event split into two frames with a ping between them.</summary>
            public void RaiseFragmentedRecordEvent(bool active)
            {
                Recording = active;
                byte[] data = Encoding.UTF8.GetBytes(MiniJson.Serialize(new Dictionary<string, object> { { "op", 5 }, { "d", new Dictionary<string, object> {
                    { "eventType", "RecordStateChanged" }, { "eventIntent", 64 },
                    { "eventData", new Dictionary<string, object> { { "outputActive", active } } } } } }));
                int half = data.Length / 2;
                var first = new List<byte> { 0x01, (byte)half };      // text, FIN=0
                first.AddRange(data.Take(half));
                var last = new List<byte> { 0x80, (byte)(data.Length - half) }; // continuation, FIN=1
                last.AddRange(data.Skip(half));
                lock (this)
                {
                    client.Write(first.ToArray(), 0, first.Count);
                    client.Write(new byte[] { 0x89, 0 }, 0, 2);
                    client.Write(last.ToArray(), 0, last.Count);
                }
            }

            private static string ReadText(NetworkStream s)
            {
                int b0 = s.ReadByte(), b1 = s.ReadByte();
                if (b0 < 0 || b1 < 0) return null;
                int len = b1 & 0x7F;
                if (len == 126) len = (s.ReadByte() << 8) | s.ReadByte();
                var mask = new byte[4];
                for (int i = 0; i < 4; i++) mask[i] = (byte)s.ReadByte();
                var data = new byte[len];
                for (int i = 0; i < len; i++) data[i] = (byte)(s.ReadByte() ^ mask[i & 3]);
                if ((b0 & 0x0F) == 0x8) return null;
                if ((b1 & 0x80) == 0) throw new InvalidDataException("client frames must be masked");
                if ((b0 & 0x0F) == 0xA) return "";
                return Encoding.UTF8.GetString(data);
            }

            public void Dispose()
            {
                stop = true;
                listener.Stop();
                thread.Join(1000);
            }
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        [Test]
        public void ReadsRecordingStateWithPasswordAndEvents()
        {
            using (var obs = new FakeObs("supersecretpassword") { Streaming = true })
            using (var client = new ObsWebSocketClient(obs.Port, "supersecretpassword", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.Connected && client.Status.Streaming), "connected and saw live state: " + client.Status.LastError);
                Assert.True(obs.IdentifiedOk);
                Assert.False(client.Status.Recording);
                Assert.Equal("5.5.4", client.Status.ObsVersion);

                obs.RaiseRecordEvent(true);
                Assert.True(WaitFor(() => client.Status.Recording), "recording event applied");
                obs.RaiseRecordEvent(false);
                Assert.True(WaitFor(() => !client.Status.Recording), "recording stop applied");

                lock (obs.RequestTypes)
                    Assert.True(obs.RequestTypes.All(r => r == "GetStreamStatus" || r == "GetRecordStatus" || r == "GetReplayBufferStatus" || r == "GetVirtualCamStatus"),
                        "only read-only status requests: " + string.Join(",", obs.RequestTypes));
            }
        }

        [Test]
        public void WrongPasswordIsReportedAndNotRetriedQuickly()
        {
            using (var obs = new FakeObs("right"))
            using (var client = new ObsWebSocketClient(obs.Port, "wrong", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.AuthenticationFailed), "auth failure detected: " + client.Status.LastError);
                Assert.False(client.Status.Connected);
                Assert.False(obs.IdentifiedOk);
            }
        }

        [Test]
        public void MissingPasswordAsksUserInsteadOfFailing()
        {
            var log = new MemoryLogSink();
            using (var obs = new FakeObs("needed"))
            using (var client = new ObsWebSocketClient(obs.Port, "", log))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.AuthenticationRequired));
                Assert.True(WaitFor(() => log.Contains("requires a password")));
                Assert.Null(obs.ReceivedAuth, "nothing sent without a password");
            }
        }

        [Test]
        public void NoAuthenticationAndUnreachableServer()
        {
            using (var obs = new FakeObs(null) { Recording = true })
            using (var client = new ObsWebSocketClient(obs.Port, "", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.Connected && client.Status.Recording));
            }

            int freePort;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            using (var client = new ObsWebSocketClient(freePort, "", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.LastError.Length > 0), "unreachable OBS reported quietly");
                Assert.False(client.Status.Connected);
            }
        }

        [Test]
        public void PingDoesNotStallPolling()
        {
            using (var obs = new FakeObs(null))
            using (var client = new ObsWebSocketClient(obs.Port, "", new MemoryLogSink()) { PollSeconds = 1 })
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.Connected), "connected");
                Thread.Sleep(300);
                int before;
                lock (obs.RequestTypes) before = obs.RequestTypes.Count;
                obs.SendPing();
                // Before the fix the client blocked up to 10 s waiting for a message after a ping and did not poll.
                Assert.True(WaitFor(() => { lock (obs.RequestTypes) return obs.RequestTypes.Count >= before + 4; }, 3500), "kept polling after a ping");
                Assert.True(client.Status.Connected);
            }
        }

        [Test]
        public void FragmentedMessageWithInterleavedPingIsAssembled()
        {
            using (var obs = new FakeObs(null))
            using (var client = new ObsWebSocketClient(obs.Port, "", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.Connected), "connected");
                obs.RaiseFragmentedRecordEvent(true);
                Assert.True(WaitFor(() => client.Status.Recording), "event assembled from fragments");
            }
        }

        [Test]
        public void ReplayBufferCountsAsCapture()
        {
            using (var obs = new FakeObs(null) { ReplayBuffer = true })
            using (var client = new ObsWebSocketClient(obs.Port, "", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.ReplayBuffer), "replay buffer state read");
                Assert.False(client.Status.Streaming || client.Status.Recording);
                var signals = new CaptureSignals { ObsWebSocketConnected = true, ObsOtherOutput = client.Status.OtherOutput };
                Assert.Equal(CaptureKind.Obs, CaptureStateMachine.Evaluate(StreamingTrigger.Auto, signals, out string reason));
                Assert.True(reason.Contains("replay buffer"), reason);
            }
        }

        [Test]
        public void KickedStateStaysVisible()
        {
            using (var obs = new FakeObs(null))
            using (var client = new ObsWebSocketClient(obs.Port, "", new MemoryLogSink()))
            {
                client.Start();
                Assert.True(WaitFor(() => client.Status.Connected), "connected");
                obs.SendClose(4011);
                Assert.True(WaitFor(() => client.Status.Kicked), "kicked state reported");
                Thread.Sleep(300);
                Assert.True(client.Status.Kicked && !client.Status.Connected, "still reported after the thread ended");
            }
        }

        [Test]
        public void RefusesNonLocalHosts()
        {
            Assert.Throws<ArgumentException>(() => new ObsWebSocketClient(4455, "", null, "192.168.1.10"));
        }

        [Test]
        public void AuthenticationMatchesProtocolDefinition()
        {
            // base64(sha256(base64(sha256(password + salt)) + challenge)), computed independently here.
            string password = "pw", salt = "salt123", challenge = "chal456";
            string expected;
            using (SHA256 sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                expected = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
            Assert.Equal(expected, ObsWebSocketClient.ComputeAuthentication(password, salt, challenge));
        }
    }

    public sealed class StreamlabsParsingTests
    {
        private static StreamlabsStatusClient Client(MemoryLogSink log = null) => new StreamlabsStatusClient(log ?? new MemoryLogSink(), 1);

        [Test]
        public void ReadsLiveRecordingAndOfflineStates()
        {
            var c = Client();
            c.Apply("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"streamingStatus\":\"live\",\"recordingStatus\":\"offline\"}}");
            Assert.True(c.Status.Connected && c.Status.Streaming && !c.Status.Recording);
            c.Apply("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"streamingStatus\":\"reconnecting\",\"recordingStatus\":\"recording\"}}");
            Assert.True(c.Status.Streaming, "reconnecting still counts as live");
            Assert.True(c.Status.Recording);
            c.Apply("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"streamingStatus\":\"starting\",\"recordingStatus\":\"stopping\"}}");
            Assert.False(c.Status.Streaming || c.Status.Recording, "transitional states are not live");
        }

        [Test]
        public void ErrorResponsesCountAsNotConnected()
        {
            // Streamlabs answers "API server is busy" while it loads; that says nothing about live/recording,
            // so the runtime must fall back to "Streamlabs is running".
            var c = Client();
            c.Apply("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32600,\"message\":\"API server is busy\"}}");
            Assert.False(c.Status.Connected);
            Assert.False(c.Status.Streaming);
            Assert.True(c.Status.LastError.Contains("busy"), c.Status.LastError);
        }

        [Test]
        public void DualOutputDisplaysAreCombined()
        {
            var c = Client();
            // Top-level fields show the display that updated last (horizontal, offline) while vertical is live.
            c.Apply("{\"result\":{\"streamingStatus\":\"offline\",\"recordingStatus\":\"offline\",\"status\":{" +
                    "\"horizontal\":{\"streaming\":\"offline\",\"recording\":\"offline\"}," +
                    "\"vertical\":{\"streaming\":\"live\",\"recording\":\"recording\"}}}}");
            Assert.True(c.Status.Connected);
            Assert.True(c.Status.Streaming, "vertical display is live");
            Assert.True(c.Status.Recording, "vertical display records");
        }

        [Test]
        public void ReplayBufferIsReported()
        {
            var c = Client();
            c.Apply("{\"result\":{\"streamingStatus\":\"offline\",\"recordingStatus\":\"offline\",\"replayBufferStatus\":\"running\"}}");
            Assert.True(c.Status.ReplayBuffer && c.Status.OtherOutput);
            Assert.False(c.Status.Streaming || c.Status.Recording);
        }

        [Test]
        public void LogsTransitionsOnce()
        {
            var log = new MemoryLogSink();
            var c = Client(log);
            string live = "{\"result\":{\"streamingStatus\":\"live\",\"recordingStatus\":\"offline\"}}";
            string idle = "{\"result\":{\"streamingStatus\":\"offline\",\"recordingStatus\":\"offline\"}}";
            c.Apply(idle);
            c.Apply(live);
            c.Apply(live);
            c.Apply(idle);
            Assert.True(log.Contains("live stream started"));
            Assert.True(log.Contains("live stream stopped"));
        }
    }

    [Integration]
    public sealed class StreamlabsClientTests
    {
        [Test]
        public void PollsLocalApiAndSendsOnlyTheStatusRequest()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var requests = new List<string>();
            var server = new Thread(() =>
            {
                try
                {
                    using (TcpClient tcp = listener.AcceptTcpClient())
                    using (NetworkStream ns = tcp.GetStream())
                    using (var reader = new StreamReader(ns, Encoding.UTF8))
                    {
                        string line = reader.ReadLine();
                        lock (requests) requests.Add(line);
                        byte[] reply = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"streamingStatus\":\"offline\",\"recordingStatus\":\"recording\"}}\n");
                        ns.Write(reply, 0, reply.Length);
                        Thread.Sleep(500);
                    }
                }
                catch
                {
                    // test teardown
                }
            }) { IsBackground = true };
            server.Start();

            var client = new StreamlabsStatusClient(new MemoryLogSink(), port);
            try
            {
                client.Start();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!client.Status.Recording && sw.ElapsedMilliseconds < 5000)
                    Thread.Sleep(20);
                Assert.True(client.Status.Connected && client.Status.Recording, "read recording state: " + client.Status.LastError);
                lock (requests)
                {
                    Assert.Equal(1, requests.Count);
                    Dictionary<string, object> req = MiniJson.Parse(requests[0]) as Dictionary<string, object>;
                    Assert.Equal("getModel", MiniJson.Str(req, "method"));
                    Assert.Equal("StreamingService", MiniJson.Str(MiniJson.Obj(req, "params"), "resource"));
                }
            }
            finally
            {
                client.Dispose();
                listener.Stop();
            }
        }
    }

    public sealed class SakuraaSupportTests
    {
        private static PluginInventory.Entry Plugin(string guid) => new PluginInventory.Entry { Guid = guid, Name = guid, Version = "1.0" };

        [Test]
        public void NotDetectedWithoutSakuraa()
        {
            var s = new SakuraaSupport();
            s.Refresh(new[] { Plugin("com.someone.othermod") }, new HarmonyFootprint.Report());
            Assert.False(s.Detected);
            Assert.Equal(0, s.Advice.Count);
        }

        [Test]
        public void DetectsByHarmonyOwnerAndGivesGtfxAdvice()
        {
            var footprint = new HarmonyFootprint.Report();
            footprint.MethodsPerOwner["com.sakuraa.client"] = 12;
            var s = new SakuraaSupport();
            s.Refresh(new PluginInventory.Entry[0], footprint);
            Assert.True(s.Detected);
            Assert.True(s.Detail.Contains("12"), s.Detail);
            Assert.True(s.Advice.Any(a => a.Contains("GTFX")), "GTFX advice present");
        }

        [Test]
        public void DetectsByPluginGuid()
        {
            var s = new SakuraaSupport();
            s.Refresh(new[] { Plugin("com.example.sakuraa.client") }, null);
            Assert.True(s.Detected);
            Assert.True(s.Detail.Contains("com.example.sakuraa.client"), s.Detail);
            s.Refresh(new PluginInventory.Entry[0], null);
            Assert.False(s.Detected, "refresh clears state");
            Assert.Equal(0, s.Advice.Count);
        }
    }

    public sealed class DesktopCameraTests
    {
        [Test]
        public void FindsTheGameMemberAndWaitsSafelyWhileTheGameIsNotReady()
        {
            var ledger = new SettingsLedger(new MemoryLogSink());
            var module = new GPU.DesktopCameraModule(Utilities.GameTypes.FromLoadedAssemblies(), ledger, new MemoryLogSink());
            Assert.True(module.Available, "GorillaTagger.Instance / thirdPersonCamera found by name");
            GorillaTagger.Instance = null;
            module.Apply(true, "test", false, 0f);
            Assert.True(module.Status.Contains("not found"), module.Status);
            Assert.Equal(0, ledger.Settings.Count, "nothing managed without a camera");
            module.Apply(false, "test", false, 1f);
            Assert.True(module.Status.StartsWith("on"), module.Status);
        }

        [Test]
        public void ReportsUnavailableWhenTheGameMemberIsMissing()
        {
            var types = Utilities.GameTypes.FromAssemblies(new[] { typeof(DesktopCameraTests).Assembly });
            var module = new GPU.DesktopCameraModule(types, new SettingsLedger(new MemoryLogSink()), new MemoryLogSink());
            Assert.False(module.Available);
            module.Apply(true, "test", false, 0f);
            Assert.True(module.Status.StartsWith("unavailable"), module.Status);
        }
    }

    public sealed class EyeResolutionTests
    {
        [Test]
        public void MeasuresMegapixelsAtScaleOne()
        {
            // The user's report: 4992x5376 per eye at render scale 0.85.
            double mp = EyeResolution.MegapixelsAtScale1(4992, 5376, 0.85f);
            Assert.Near(37.1, mp, 0.1);
            Assert.Equal(0.0, EyeResolution.MegapixelsAtScale1(0, 5376, 0.85f), "no headset");
        }

        [Test]
        public void CapsRenderScaleToTheMegapixelLimit()
        {
            double mp = EyeResolution.MegapixelsAtScale1(4992, 5376, 0.85f);
            float scale = EyeResolution.MaxScaleFor(mp, 8f);
            Assert.Near(0.46, scale, 0.005);
            double rendered = mp * scale * scale;
            Assert.True(rendered <= 8.0, "within the cap: " + rendered);
            Assert.True(rendered > 7.0, "not lower than needed: " + rendered);
            // A normal Quest 3 eye (4.6 MP) is below the cap: no change.
            Assert.True(EyeResolution.MaxScaleFor(EyeResolution.Quest3DefaultMegapixels, 8f) > 1f);
            Assert.Equal(float.MaxValue, EyeResolution.MaxScaleFor(mp, 0f), "0 = no cap");
            Assert.Equal(EyeResolution.MinimumScale, EyeResolution.MaxScaleFor(400, 1f), "never below the floor");
        }
    }

    public sealed class ReShadeAdvisorTests
    {
        private const string Preset =
            "PreprocessorDefinitions=\r\n" +
            "Techniques=SMAA@SMAA.fx,MXAO@qUINT_mxao.fx,CinematicDOF@CinematicDOF.fx,LumaSharpen@LumaSharpen.fx,DisplayDepth@DisplayDepth.fx\r\n" +
            "TechniqueSorting=SMAA@SMAA.fx,MXAO@qUINT_mxao.fx,CinematicDOF@CinematicDOF.fx,LumaSharpen@LumaSharpen.fx,DisplayDepth@DisplayDepth.fx\r\n" +
            "\r\n[qUINT_mxao.fx]\r\nMXAO_SAMPLE_RADIUS=2.5\r\n";

        private const string CleanPreset = "Techniques=SMAA@SMAA.fx,Vibrance@Vibrance.fx\r\n";

        private static string TempGame()
        {
            string dir = Path.Combine(Path.GetTempPath(), "crix-reshade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Write(string dir, string name, string text) => File.WriteAllText(Path.Combine(dir, name), text);

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        [Test]
        public void FlagsUsuallyExpensiveEffectsAndPerformanceMode()
        {
            string game = TempGame();
            try
            {
                // ReShade writes Windows paths with a backslash; the advisor handles both separators.
                Write(game, "ReShade.ini", "[GENERAL]\r\nPerformanceMode=0\r\nPresetPath=.\\ReShadePreset.ini\r\n");
                Write(game, "ReShadePreset.ini", Preset);
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.True(a.Found);
                Assert.Equal("", a.Error);
                Assert.Equal(1, a.Runtimes.Count, "no ReShadeVR.ini: desktop runtime only");
                Assert.Equal(Path.Combine(game, "ReShadePreset.ini"), a.PresetPath);
                Assert.Equal(ReShade.ReShadePresetAdvisor.FromPresetPath, a.Primary.PresetSource);
                Assert.Equal(false, a.PerformanceMode);
                Assert.Equal(5, a.Enabled.Count);
                var heavy = a.Heavy.Select(t => t.Name).ToList();
                Assert.True(heavy.Contains("MXAO") && heavy.Contains("CinematicDOF") && heavy.Contains("DisplayDepth"), string.Join(",", heavy));
                Assert.False(heavy.Contains("SMAA") || heavy.Contains("LumaSharpen"), "cheap effects are not flagged");
                Assert.True(a.Advice.Any(l => l.Contains("Performance Mode is off")));
                Assert.True(a.Advice.Any(l => l.Contains("MXAO")));
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void LightPresetIsACopyWithoutHeavyEffects()
        {
            string game = TempGame();
            try
            {
                Write(game, "ReShade.ini", "[GENERAL]\nPerformanceMode=1\nPresetPath=ReShadePreset.ini\n");
                string presetPath = Path.Combine(game, "ReShadePreset.ini");
                File.WriteAllText(presetPath, Preset);
                string iniBefore = File.ReadAllText(Path.Combine(game, "ReShade.ini"));
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.Equal(true, a.PerformanceMode);
                var candidates = a.LightPresetCandidates();
                Assert.Equal(1, candidates.Count);
                string written = a.WriteLightPreset(candidates[0]);
                Assert.Equal(Path.Combine(game, "ReShadePreset.CRIX-light.ini"), written);
                Assert.Equal(Preset, File.ReadAllText(presetPath), "the user's preset is unchanged");
                Assert.Equal(iniBefore, File.ReadAllText(Path.Combine(game, "ReShade.ini")), "ReShade.ini is unchanged");
                string light = File.ReadAllText(written);
                Assert.True(light.Contains("Techniques=SMAA@SMAA.fx,LumaSharpen@LumaSharpen.fx\r\n"), light);
                Assert.True(light.Contains("MXAO_SAMPLE_RADIUS=2.5"), "effect settings kept");
                Assert.True(light.Contains("TechniqueSorting=SMAA@SMAA.fx,MXAO"), "sorting line kept as is");
                var copy = new ReShade.ReShadePresetAdvisor();
                copy.AnalyzePresetText(light);
                Assert.Equal(2, copy.Enabled.Count);
                Assert.False(copy.Heavy.Any(), "no heavy effect left in the copy");
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void HandlesMissingFilesAndOldPresetFormat()
        {
            string game = TempGame();
            try
            {
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.False(a.Found, "no ReShade.ini");
                a.AnalyzePresetText("Techniques=MXAO,Vibrance\n");
                Assert.Equal(2, a.Enabled.Count);
                Assert.Equal("MXAO", a.Enabled[0].Name);
                Assert.True(a.Enabled[0].HeavyReason != null && a.Enabled[1].HeavyReason == null);
                Assert.Throws<InvalidOperationException>(() => a.WriteLightPreset(a.Primary));
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void HeadsetConfigOverridesPerKeyAndIsPreferred()
        {
            string game = TempGame();
            try
            {
                // ReShade 6: the headset runtime reads ReShadeVR.ini, each missing key falls back to ReShade.ini.
                Write(game, "ReShade.ini", "[GENERAL]\nPerformanceMode=1\nPresetPath=.\\Clean.ini\n");
                Write(game, "ReShadeVR.ini", "[GENERAL]\nPresetPath=.\\VR.ini\n");
                Write(game, "Clean.ini", CleanPreset);
                Write(game, "VR.ini", "Techniques=MXAO@qUINT_mxao.fx,RTGlobalIllumination@qUINT_rtgi.fx,SMAA@SMAA.fx\n");
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.Equal(2, a.Runtimes.Count);
                Assert.True(a.Primary.IsVr, "the headset runtime is the primary one");
                Assert.Equal(Path.Combine(game, "VR.ini"), a.PresetPath);
                Assert.Equal(true, a.PerformanceMode, "PerformanceMode falls back to ReShade.ini");
                Assert.Equal(2, a.Heavy.Count());
                Assert.True(a.Advice.Any(l => l.Contains("headset") && l.Contains("MXAO") && l.Contains("for each eye")), string.Join(" | ", a.Advice));
                var candidates = a.LightPresetCandidates();
                Assert.Equal(1, candidates.Count, "only the headset preset has expensive effects");
                Assert.Equal(Path.Combine(game, "VR.CRIX-light.ini"), a.WriteLightPreset(candidates[0]));

                Write(game, "ReShadeVR.ini", "[GENERAL]\nPresetPath=.\\VR.ini\nPerformanceMode=0\n");
                a.Analyze(game);
                Assert.Equal(false, a.PerformanceMode, "the headset's own PerformanceMode wins");
                Assert.Equal(true, a.Runtimes.First(r => !r.IsVr).PerformanceMode);
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void ResolvesThePresetLikeReShade()
        {
            string game = TempGame();
            try
            {
                Write(game, "Heavy.ini", Preset);
                Write(game, "Clean.ini", CleanPreset);
                Write(game, "ReShadePreset.ini", CleanPreset);
                Write(game, "NotAPreset.ini", "[Something]\nKey=1\n");
                var a = new ReShade.ReShadePresetAdvisor();

                Write(game, "ReShade.ini", "[GENERAL]\nStartupPresetPath=.\\Heavy.ini\nPresetPath=.\\Clean.ini\n");
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "Heavy.ini"), a.PresetPath, "a valid start-up preset wins");
                Assert.Equal(ReShade.ReShadePresetAdvisor.FromStartupPreset, a.Primary.PresetSource);
                Assert.True(a.Advice.Any(l => l.Contains("start-up preset")), "advice says to change the start-up preset too");

                Write(game, "ReShade.ini", "[GENERAL]\nPerformanceMode=1\n");
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "ReShadePreset.ini"), a.PresetPath, "no PresetPath: ReShade's default");
                Assert.Equal(ReShade.ReShadePresetAdvisor.FromDefault, a.Primary.PresetSource);

                Write(game, "ReShade.ini", "[GENERAL]\nPresetPath=.\\NotAPreset.ini\n");
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "ReShadePreset.ini"), a.PresetPath, "a file without Techniques is not a preset");

                Write(game, "ReShade.ini", "[GENERAL]\nPresetPath=.\\Clean.fx\n");
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "ReShadePreset.ini"), a.PresetPath, "only .ini/.txt presets");

                Write(game, "ReShade.ini", "[GENERAL]\nStartupPresetPath=.\\Missing.txt\n");
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "Missing.txt"), a.PresetPath, "a preset that does not exist yet is valid (new preset)");
                Assert.False(a.Primary.PresetExists);
                Assert.Equal(0, a.LightPresetCandidates().Count);
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void EscapedCommasCommentsAndRepeatedKeys()
        {
            string game = TempGame();
            try
            {
                // ReShade doubles commas inside a value; "/" starts a comment; a repeated key adds to its value.
                Write(game, "ReShade.ini", "// written by ReShade\n[GENERAL]\nPresetPath=.\\Clean,, Sharp.ini\n");
                Write(game, "Clean, Sharp.ini", "Techniques=X@Foo,, Bar.fx,MXAO@qUINT_mxao.fx\nTechniques=Vibrance@Vibrance.fx\n");
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "Clean, Sharp.ini"), a.PresetPath);
                Assert.True(a.Primary.PresetExists);
                Assert.Equal(3, a.Enabled.Count);
                Assert.Equal("Foo, Bar.fx", a.Enabled[0].File);
                Assert.Equal("Vibrance", a.Enabled[2].Name);
                string light = ReShade.ReShadePresetAdvisor.MakeLightPresetText(File.ReadAllText(Path.Combine(game, "Clean, Sharp.ini")), out int removed);
                Assert.Equal(1, removed);
                Assert.True(light.StartsWith("Techniques=X@Foo,, Bar.fx\r\n"), light);

                Assert.True(ReShade.ReShadePresetAdvisor.SplitElements("a,,,b").SequenceEqual(new[] { "a,", "b" }), "',,' then ',' splits like ReShade");
                Assert.True(ReShade.ReShadePresetAdvisor.SplitElements("a,").SequenceEqual(new[] { "a", "" }), "trailing comma gives an empty element");
            }
            finally
            {
                Cleanup(game);
            }
        }

        [Test]
        public void FollowsReShadeBasePathOverride()
        {
            string game = TempGame();
            try
            {
                Directory.CreateDirectory(Path.Combine(game, "reshade-files"));
                Write(game, "ReShade.ini", "[INSTALL]\nBasePath=.\\reshade-files\n");
                Write(Path.Combine(game, "reshade-files"), "ReShade.ini", "[GENERAL]\nPresetPath=.\\P.ini\nPerformanceMode=0\n");
                Write(Path.Combine(game, "reshade-files"), "P.ini", Preset);
                var a = new ReShade.ReShadePresetAdvisor();
                a.Analyze(game);
                Assert.Equal(Path.Combine(game, "reshade-files"), a.BasePath);
                Assert.Equal(Path.Combine(game, "reshade-files", "P.ini"), a.PresetPath);
                Assert.Equal(false, a.PerformanceMode);
            }
            finally
            {
                Cleanup(game);
            }
        }
    }

    public sealed class SpotifyDetectionTests
    {
        [Test]
        public void SpotifyIsClassifiedAndDescribed()
        {
            CompanionApp apps = CompanionApps.ClassifyProcesses(new[] { "Spotify", "explorer" });
            Assert.True((apps & CompanionApp.Spotify) != 0);
            Assert.True(CompanionApps.Describe(apps).Contains("Spotify"));
            Assert.False(CompanionApps.Suggest(CompanionApp.Spotify).HasValue, "Spotify alone does not switch profiles");
        }
    }
}
