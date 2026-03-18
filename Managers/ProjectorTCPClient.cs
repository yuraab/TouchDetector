using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using ProjectorShared;
using System.Net;

namespace CPRTouchVision.Projector
{
    public class ProjectorTcpClient
    {
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
