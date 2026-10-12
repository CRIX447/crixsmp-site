// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Streaming
{
    /// <summary>
    /// Read-only Streamlabs Desktop status reader. Streamlabs Desktop serves newline-delimited JSON-RPC 2.0 on
    /// 127.0.0.1:28194 and trusts local clients without a token. This client only ever calls
    /// StreamingService.getModel and reads streamingStatus / recordingStatus. Background thread; the game reads
    /// <see cref="Status"/>.
    /// </summary>
    public sealed class StreamlabsStatusClient : IDisposable
    {
        public const int DefaultPort = 28194;
        private const string Request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getModel\",\"params\":{\"resource\":\"StreamingService\"}}\n";

        private readonly int port;
        private readonly ILogSink log;
        private readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        private Thread thread;
        private volatile bool stopping;
        private volatile ObsStatus status = ObsStatus.Disconnected;

        public StreamlabsStatusClient(ILogSink log, int port = DefaultPort)
        {
            this.port = port;
            this.log = log ?? NullLogSink.Instance;
        }

        public ObsStatus Status => status;
        public int PollSeconds { get; set; } = 5;

        public void Start()
        {
            if (thread != null)
                return;
            thread = new Thread(Run) { IsBackground = true, Name = "CRIX Streamlabs status" };
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            stopSignal.Set();
        }

        public void Dispose()
        {
            Stop();
            try { thread?.Join(2000); } catch { /* background thread */ }
            thread = null;
        }

        private void Run()
        {
            int retry = 5;
            bool wasConnected = false;
            while (!stopping)
            {
                try
                {
                    using (var tcp = new TcpClient())
                    {
                        IAsyncResult connect = tcp.BeginConnect("127.0.0.1", port, null, null);
                        if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)) || !tcp.Connected)
                            throw new IOException("Streamlabs Desktop API not reachable");
                        tcp.EndConnect(connect);
                        NetworkStream stream = tcp.GetStream();
                        stream.ReadTimeout = 5000;
                        retry = 5;
                        while (!stopping)
                        {
                            byte[] request = Encoding.UTF8.GetBytes(Request);
                            stream.Write(request, 0, request.Length);
                            string line = ReadLine(stream);
                            if (line == null)
                                throw new IOException("Streamlabs closed the connection");
                            Apply(line);
                            if (!wasConnected)
                            {
                                wasConnected = true;
                                log.Info("Connected to Streamlabs Desktop's local API (read-only: streaming/recording state).");
                            }
                            if (stopSignal.WaitOne(TimeSpan.FromSeconds(PollSeconds)))
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    status = new ObsStatus { LastError = e.Message };
                    if (wasConnected)
                        log.Info("Streamlabs Desktop API disconnected.");
                    wasConnected = false;
                }
                if (stopping || stopSignal.WaitOne(TimeSpan.FromSeconds(retry)))
                    break;
                retry = Math.Min(60, retry * 2);
            }
            status = ObsStatus.Disconnected;
        }

        /// <summary>Parses one JSON-RPC response line into the status snapshot. Internal for tests.</summary>
        internal void Apply(string line)
        {
            object message = MiniJson.Parse(line);
            Dictionary<string, object> result = MiniJson.Obj(message, "result");
            if (result == null)
            {
                status = new ObsStatus { Connected = true, LastError = "unexpected Streamlabs response" };
                return;
            }
            string stream = MiniJson.Str(result, "streamingStatus") ?? "offline";
            string record = MiniJson.Str(result, "recordingStatus") ?? "offline";
            ObsStatus before = status;
            var after = new ObsStatus
            {
                Connected = true,
                ObsVersion = "Streamlabs Desktop",
                Streaming = stream == "live" || stream == "reconnecting",
                Recording = record == "recording",
            };
            status = after;
            if (before.Connected && before.Streaming != after.Streaming)
                log.Info(after.Streaming ? "Streamlabs reports: live stream started." : "Streamlabs reports: live stream stopped.");
            if (before.Connected && before.Recording != after.Recording)
                log.Info(after.Recording ? "Streamlabs reports: recording started." : "Streamlabs reports: recording stopped.");
        }

        private static string ReadLine(Stream stream)
        {
            var bytes = new List<byte>(256);
            while (bytes.Count < 1024 * 1024)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : null;
                if (b == '\n')
                    return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add((byte)b);
            }
            throw new InvalidDataException("Streamlabs response too large");
        }
    }
}
