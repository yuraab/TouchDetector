using CPRProjectorShared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPRTouchVision.Projector
{
    public class ProjectorManager
    {
        private readonly string _exePath;
        private Process _process;

        public ProjectorManager(string? exePath = null)
        {
            if (string.IsNullOrEmpty(exePath))
                _exePath = Default.ProjectorAppName + ".exe";
            else _exePath = exePath;
        }

        public Task StartAsync()
        {
            if (_process != null && !_process.HasExited)
                return Task.CompletedTask;

            Debug.WriteLine($"Starting projector helper from '{_exePath}'");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _process.Start();
            return Task.CompletedTask;
        }

        public async Task StartProjectorAsync()
        {
            // 1. Start listener BEFORE starting the process
            var listener = new TcpListener(IPAddress.Loopback, 5001);
            listener.Start();

            // 2. Start helper EXE
            await StartAsync();

            // 3. Wait for helper to connect
            _client = await listener.AcceptTcpClientAsync();

            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            // 4. Wait for READY
            string ready = await _reader.ReadLineAsync();
            Debug.WriteLine($"Received '{ready}' from projector");

            if (ready != Events.Ready)
                throw new Exception("Projector not ready");

            await _writer.WriteLineAsync(Events.Ack_Ready);

            Debug.WriteLine($"Sent 'ACK_READY' to projector");
        }

        public async void StopProjector()
        {
            await SendStopSafeAsync();

            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill();
            }
            catch { }
        }

        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;

        public async Task ConnectAsync()
        {
            if (_client != null && _client.Connected)
                return;

            var listener = new TcpListener(IPAddress.Loopback, 5001);
            listener.Start();
            //Console.WriteLine("HELPER: Listening on port 5001");

            _client = await listener.AcceptTcpClientAsync();

            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            // Wait for READY
            string ready = await _reader.ReadLineAsync();
            Debug.WriteLine($"Received '{ready}' from projector");
            if (ready != Events.Ready)
                throw new Exception("Projector not ready");
        }

        public Task SendShowAsync(string path) =>
            _writer.WriteLineAsync($"{Commands.Show}|{path}");

        public Task SendStopAsync() =>
            _writer.WriteLineAsync(Commands.Stop);

        public async Task WaitForAsync(string expected, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string line = await _reader.ReadLineAsync();
                if (line == expected)
                    return;
            }
        }

        public async Task SendStopSafeAsync()
        {
            try { await SendStopAsync(); } catch { }
        }
    }

}

