using System.Net;

namespace Vstup_Predictor.Services
{
    public class ProxyHttpClientFactory
    {
        private readonly List<(HttpClient Client, string Proxy, bool Active)> _clients = new();
        private int _index = 0;
        private readonly object _lock = new();
        private readonly Random _random = new();

        // User agent rotation pool
        private readonly string[] _userAgents = {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Edge/120.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:120.0) Gecko/20100101 Firefox/120.0"
    };

        public int ClientsCount { get => _clients.Count; }

        public ProxyHttpClientFactory(string proxyFilePath)
        {
            if (!File.Exists(proxyFilePath))
                throw new FileNotFoundException("Proxies file not found", proxyFilePath);

            var lines = File.ReadAllLines(proxyFilePath)
                            .Where(l => !string.IsNullOrWhiteSpace(l));

            foreach (var line in lines)
            {
                try
                {
                    var parts = line.Trim().Split(':');
                    if (parts.Length != 4) continue;

                    var host = parts[0];
                    var port = int.Parse(parts[1]);
                    var username = parts[2];
                    var password = parts[3];

                    var proxy = new WebProxy($"{host}:{port}", true)
                    {
                        Credentials = new NetworkCredential(username, password)
                    };

                    var handler = new HttpClientHandler
                    {
                        Proxy = proxy,
                        UseProxy = true
                    };

                    var client = new HttpClient(handler, disposeHandler: true)
                    {
                        Timeout = TimeSpan.FromSeconds(30)
                    };

                    // Set random user agent for each client
                    var randomUserAgent = _userAgents[_random.Next(_userAgents.Length)];
                    client.DefaultRequestHeaders.Add("User-Agent", randomUserAgent);

                    // Add common browser headers to appear more legitimate
                    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    client.DefaultRequestHeaders.Add("DNT", "1");
                    client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                    client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                    // Randomize some headers
                    if (_random.Next(2) == 0)
                    {
                        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                        client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                    }

                    _clients.Add((client, $"{host}:{port}", true));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create client for proxy {line}: {ex.Message}");
                }
            }

            if (!_clients.Any())
                throw new InvalidOperationException("No valid proxies found in file.");
        }

        public HttpClient GetHttpClient()
        {
            lock (_lock)
            {
                var available = _clients.Where(c => c.Active).ToList();
                if (!available.Any())
                    throw new InvalidOperationException("No active proxies left.");

                var clientTuple = available[_index % available.Count];
                _index++;
                return clientTuple.Client;
            }
        }

        public void Deactivate(HttpClient client)
        {
            lock (_lock)
            {
                for (int i = 0; i < _clients.Count; i++)
                {
                    if (_clients[i].Client == client)
                    {
                        _clients[i] = (_clients[i].Client, _clients[i].Proxy, false);
                        Console.WriteLine($"⚠️ Proxy {_clients[i].Proxy} deactivated.");
                        break;
                    }
                }
            }
        }

        // Method to add random delay between requests
        public static async Task RandomDelay(int minMs = 1000, int maxMs = 5000)
        {
            var random = new Random();
            var delay = random.Next(minMs, maxMs);
            await Task.Delay(delay);
        }

        // Method to add additional headers per request
        public void AddRequestSpecificHeaders(HttpRequestMessage request, string referer = null)
        {
            // Add referer if provided
            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.Add("Referer", referer);
            }

            // Add cache control randomly
            if (_random.Next(3) == 0)
            {
                request.Headers.Add("Cache-Control", "no-cache");
            }
            else if (_random.Next(2) == 0)
            {
                request.Headers.Add("Cache-Control", "max-age=0");
            }

            // Randomly add pragma
            if (_random.Next(4) == 0)
            {
                request.Headers.Add("Pragma", "no-cache");
            }
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Client?.Dispose();
            }
            _clients.Clear();
        }
    }
}
