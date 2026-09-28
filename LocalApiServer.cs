using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClipboardTrail
{
    internal sealed class LocalApiServer : IDisposable
    {
        private readonly HistoryStore store;
        private readonly LibraryStore library;
        private TcpListener listener;
        private Thread thread;
        private volatile bool stopping;

        public LocalApiServer(HistoryStore store, LibraryStore library)
        {
            this.store = store;
            this.library = library;
        }

        public void Start(int port)
        {
            Stop();
            stopping = false;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            thread = new Thread(Run) { IsBackground = true, Name = "Clypsera Local API" };
            thread.Start();
        }

        private void Run()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
                }
                catch { if (!stopping) Thread.Sleep(200); }
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    string first = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(first)) return;
                    string[] parts = first.Split(' ');
                    if (parts.Length < 2 || parts[0] != "GET") { Send(stream, 405, "{\"error\":\"method_not_allowed\"}"); return; }
                    string path = parts[1];
                    if (path.StartsWith("/api/latest"))
                    {
                        var list = store.Snapshot();
                        ClipboardItem latest = list.Count == 0 ? null : list[list.Count - 1];
                        Send(stream, 200, AppData.CreateSerializer().Serialize(latest));
                    }
                    else if (path.StartsWith("/api/items"))
                    {
                        int limit = ParseLimit(path);
                        var list = store.Snapshot();
                        if (list.Count > limit) list = list.GetRange(list.Count - limit, limit);
                        list.Reverse();
                        Send(stream, 200, AppData.CreateSerializer().Serialize(list));
                    }
                    else if (path.StartsWith("/api/status"))
                        Send(stream, 200, "{\"name\":\"Clypsera\",\"status\":\"ok\"}");
                    else if (path.StartsWith("/api/tree"))
                        Send(stream, 200, AppData.CreateSerializer().Serialize(library == null ? new LibraryNode[0] : library.Snapshot().ToArray()));
                    else Send(stream, 404, "{\"error\":\"not_found\"}");
                }
            }
        }

        private static int ParseLimit(string path)
        {
            int result = 100;
            int pos = path.IndexOf("limit=", StringComparison.OrdinalIgnoreCase);
            if (pos >= 0)
            {
                string raw = path.Substring(pos + 6).Split('&')[0];
                int parsed;
                if (int.TryParse(raw, out parsed)) result = Math.Max(1, Math.Min(1000, parsed));
            }
            return result;
        }

        private static void Send(NetworkStream stream, int status, string body)
        {
            byte[] content = Encoding.UTF8.GetBytes(body == null ? "null" : body);
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Method Not Allowed";
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + content.Length + "\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(content, 0, content.Length);
        }

        public void Stop()
        {
            stopping = true;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        public void Dispose() { Stop(); }
    }
}
