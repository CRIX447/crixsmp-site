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
                                    bool active = type == "GetStreamStatus" ? Streaming : Recording;
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

            private void SendClose(int code)
            {
                lock (this) client.Write(new byte[] { 0x88, 2, (byte)(code >> 8), (byte)code }, 0, 4);
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
                    Assert.True(obs.RequestTypes.All(r => r == "GetStreamStatus" || r == "GetRecordStatus"), "only read-only requests: " + string.Join(",", obs.RequestTypes));
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
        public void ErrorResponsesKeepConnectionButNoState()
        {
            var c = Client();
            c.Apply("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32600,\"message\":\"bad\"}}");
            Assert.True(c.Status.Connected);
            Assert.False(c.Status.Streaming);
            Assert.True(c.Status.LastError.Length > 0);
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
            Assert.False(s.UnofficialRebuild);
            Assert.True(s.Detail.Contains("12"), s.Detail);
            Assert.True(s.Advice.Any(a => a.Contains("GTFX")), "GTFX advice present");
        }

        [Test]
        public void FlagsUnofficialRebuildGuids()
        {
            var s = new SakuraaSupport();
            s.Refresh(new[] { Plugin("com.dusted.sakuraa.standalone") }, null);
            Assert.True(s.Detected);
            Assert.True(s.UnofficialRebuild);
            Assert.True(s.Advice.Any(a => a.Contains("unofficial")));
            s.Refresh(new PluginInventory.Entry[0], null);
            Assert.False(s.Detected, "refresh clears state");
            Assert.False(s.UnofficialRebuild);
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
