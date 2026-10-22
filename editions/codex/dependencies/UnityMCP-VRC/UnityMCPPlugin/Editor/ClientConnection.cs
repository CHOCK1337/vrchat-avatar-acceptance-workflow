using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMCP.Editor
{
    // One HTTP request/response exchange over a raw TCP socket.
    //
    // The plugin serves a tiny HTTP/1.1 endpoint (one request per connection, Connection: close)
    // rather than a persistent WebSocket: a tool call is a POST with a JSON body, the result is the
    // JSON response, then the socket closes. Stateless request/response means a domain reload is just
    // "the next request retries" - there's no long-lived socket (and no idle blocked receive thread)
    // to tear down, which is what the WebSocket model kept tripping over. HTTP is hand-rolled over
    // TcpListener because Unity's Mono runtime doesn't reliably implement HttpListener.
    internal sealed class ClientConnection
    {
        private const int MaxHeaderBytes = 16384;
        private const int MaxBodyBytes = 64 * 1024 * 1024; // guard against a bogus Content-Length

        private readonly TcpClient tcp;
        private readonly NetworkStream stream;

        public string RemoteEndPoint { get; }

        private ClientConnection(TcpClient tcp, NetworkStream stream)
        {
            this.tcp = tcp;
            this.stream = stream;
            try { RemoteEndPoint = tcp.Client.RemoteEndPoint?.ToString(); } catch { /* ignore */ }
            if (string.IsNullOrEmpty(RemoteEndPoint)) RemoteEndPoint = "unknown";
        }

        public static ClientConnection Create(TcpClient tcp)
        {
            tcp.NoDelay = true;
            // Abortive close: Close() sends a RST and discards buffers immediately rather than doing a
            // graceful FIN, so teardown never waits on the network even if the peer stopped reading.
            // Exchanges are short-lived, so this only matters in the rare reload-mid-request window.
            try { tcp.LingerState = new LingerOption(true, 0); } catch { /* ignore */ }
            return new ClientConnection(tcp, tcp.GetStream());
        }

        // Reads one HTTP request and returns its method (e.g. "POST", "GET") and body (the JSON
        // command, or "" when there's no body). Returns null if the connection closed or the request
        // was malformed/oversized. Reads the request line + headers up to the blank line (one byte at a
        // time so we never over-read into the body), then Content-Length bytes of body.
        public async Task<(string method, string body)?> ReadRequestAsync(CancellationToken token)
        {
            string headerText = await ReadHeadersAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headerText)) return null;

            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // Request line: "METHOD SP request-target SP HTTP-version".
            string method = lines[0].Split(' ')[0].ToUpperInvariant();

            int contentLength = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
                    break;
                }
            }

            if (contentLength > MaxBodyBytes) return null;
            if (contentLength <= 0) return (method, "");    // no body (e.g. a health GET, or a POST
                                                            // that omitted Content-Length)

            var body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = await stream.ReadAsync(body, read, contentLength - read, token).ConfigureAwait(false);
                if (n == 0) return null;                    // closed before the full body arrived
                read += n;
            }
            return (method, Encoding.UTF8.GetString(body));
        }

        // Writes a JSON HTTP response and flushes (e.g. statusCode 200 "OK", 400 "Bad Request").
        // Content-Length always reflects the body; pass includeBody=false for a HEAD request, which
        // gets the same status and headers but no body.
        public async Task WriteJsonResponseAsync(int statusCode, string statusText, string json, bool includeBody, CancellationToken token)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(json ?? "");
            var header =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            if (includeBody && bodyBytes.Length > 0)
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        public void Close()
        {
            try { tcp.Close(); } catch { /* ignore */ }
        }

        // Reads bytes one at a time until the CRLFCRLF that ends the HTTP headers, so we stop exactly
        // at the header/body boundary and don't consume any body bytes. Returns null on close/overflow.
        private async Task<string> ReadHeadersAsync(CancellationToken token)
        {
            var bytes = new List<byte>(512);
            var one = new byte[1];
            while (bytes.Count < MaxHeaderBytes)
            {
                int n = await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false);
                if (n == 0) return null; // closed
                bytes.Add(one[0]);
                int c = bytes.Count;
                if (c >= 4 && bytes[c - 1] == (byte)'\n' && bytes[c - 2] == (byte)'\r' &&
                    bytes[c - 3] == (byte)'\n' && bytes[c - 4] == (byte)'\r')
                {
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }
            }
            return null; // headers too large
        }
    }
}
