using AngleSharp;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using Vstup_Predictor.Models;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Vstup_Predictor.Services
{
    public class VstupParserService
    {
        private readonly ProxyHttpClientFactory _factory;
        private readonly ApplicationDbContext _db;
        private readonly string _baseUrl;
        private readonly IBrowsingContext _context;

        // Progress tracking
        public event Action<ProgressUpdate>? OnProgressUpdate;
        public event Action<RequestLog>? OnRequestLog;

        private int _totalCities = 0;
        private int _totalUniversities = 0;
        private int _totalOffers = 0;
        private int _totalApplications = 0;

        private int _parsedCities = 0;
        private int _parsedUniversities = 0;
        private int _parsedOffers = 0;
        private int _parsedApplications = 0;

        public VstupParserService(
            ProxyHttpClientFactory factory,
            ApplicationDbContext db,
            IConfiguration config)
        {
            _factory = factory;
            _db = db;
            _baseUrl = config["Vstup:BaseUrl"]!;
            _context = BrowsingContext.New(Configuration.Default);
        }
        private async Task<T> ExecuteWithProxyRetry<T>(
            Func<HttpClient, Task<T>> action,
            string requestUrl,
            string requestType,
            CancellationToken cancellationToken = default)
        {
            var random = new Random();

            for (int attempt = 0; attempt < _factory.ClientsCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                HttpClient client = null;
                var startTime = DateTime.Now;

                try
                {
                    client = _factory.GetHttpClient();

                    // Add intelligent delay between attempts
                    if (attempt > 0)
                    {
                        var delayMs = CalculateRetryDelay(attempt, random);
                        LogRequest(requestUrl, requestType, "Delaying", $"Waiting {delayMs}ms before retry {attempt + 1}", startTime);
                        await Task.Delay(delayMs, cancellationToken);
                    }

                    LogRequest(requestUrl, requestType, "Pending", $"Attempt {attempt + 1}/{_factory.ClientsCount}", startTime);

                    var result = await action(client);

                    LogRequest(requestUrl, requestType, "Success", $"Completed on attempt {attempt + 1}", startTime);
                    return result;
                }
                catch (HttpRequestException ex)
                {
                    if (client != null)
                        _factory.Deactivate(client);

                    var errorType = ClassifyHttpError(ex);
                    LogRequest(requestUrl, requestType, "Failed",
                        $"Attempt {attempt + 1}: {errorType} - {ex.Message}", startTime);

                    Console.WriteLine($"🚫 Proxy failed ({errorType}) on attempt {attempt + 1}: {ex.Message}");

                    // Add longer delay for blocking errors
                    if (errorType.Contains("Blocked") && attempt < _factory.ClientsCount - 1)
                    {
                        var blockDelay = 5000 + (attempt * 2000) + random.Next(1000, 5000);
                        await Task.Delay(blockDelay, cancellationToken);
                    }
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    LogRequest(requestUrl, requestType, "Cancelled", "User cancelled", startTime);
                    throw;
                }
                catch (TaskCanceledException ex) // timeout from HttpClient
                {
                    if (client != null)
                        _factory.Deactivate(client);

                    LogRequest(requestUrl, requestType, "Timeout",
                        $"Attempt {attempt + 1}: {ex.Message}", startTime);

                    Console.WriteLine($"⏰ Proxy timeout on attempt {attempt + 1}. Retrying...");

                    // Moderate delay after timeout
                    if (attempt < _factory.ClientsCount - 1)
                    {
                        var timeoutDelay = 2000 + (attempt * 1000) + random.Next(500, 2000);
                        await Task.Delay(timeoutDelay, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    if (client != null)
                        _factory.Deactivate(client);

                    LogRequest(requestUrl, requestType, "Error",
                        $"Attempt {attempt + 1}: Unexpected - {ex.Message}", startTime);

                    Console.WriteLine($"❌ Unexpected error on attempt {attempt + 1}: {ex.Message}");
                }
            }

            LogRequest(requestUrl, requestType, "Failed",
                $"All {_factory.ClientsCount} proxies exhausted", DateTime.Now);
            throw new Exception($"All {_factory.ClientsCount} proxies failed for {requestUrl}");
        }

        // Enhanced HTTP methods with anti-detection
        private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithProxyRetry(async client =>
            {
                // Create request with anti-detection headers
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAntiDetectionHeaders(request, url);

                var response = await client.SendAsync(request, cancellationToken);

                // Handle specific status codes that indicate blocking
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException("403 Forbidden - Proxy likely blocked");
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("429 Too Many Requests - Rate limited");
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new HttpRequestException("401 Unauthorized - Access denied");
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();

            }, url, "GET HTML", cancellationToken);
        }

        private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithProxyRetry(async client =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAntiDetectionHeaders(request, url);

                // Add JSON-specific headers
                request.Headers.Add("Accept", "application/json, text/plain, */*");

                var response = await client.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException("403 Forbidden - API access blocked");
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("429 Too Many Requests - API rate limited");
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            }, url, "GET JSON", cancellationToken);
        }

        private async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithProxyRetry(async client =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                AddAntiDetectionHeaders(request, url);

                // Add POST-specific headers
                if (content?.Headers.ContentType?.MediaType == "application/json")
                {
                    request.Headers.Add("Accept", "application/json");
                }

                var response = await client.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException("403 Forbidden - POST blocked");
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("429 Too Many Requests - POST rate limited");
                }

                return response; // Don't ensure success - let caller handle status codes

            }, url, "POST", cancellationToken);
        }

        // Helper methods
        private void AddAntiDetectionHeaders(HttpRequestMessage request, string url)
        {
            var random = new Random();

            // Add referer simulation
            var referer = GenerateSmartReferer(url);
            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.Add("Referer", referer);
            }

            // Add cache control variation
            var cacheOptions = new[] { "no-cache", "max-age=0", "no-store" };
            if (random.Next(3) == 0)
            {
                request.Headers.Add("Cache-Control", cacheOptions[random.Next(cacheOptions.Length)]);
            }

            // Occasionally add pragma
            if (random.Next(4) == 0)
            {
                request.Headers.Add("Pragma", "no-cache");
            }

            // Add sec-fetch headers occasionally (modern browsers)
            if (random.Next(2) == 0)
            {
                request.Headers.Add("Sec-Fetch-Dest", "document");
                request.Headers.Add("Sec-Fetch-Mode", "navigate");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
            }
        }

        private string GenerateSmartReferer(string currentUrl)
        {
            try
            {
                var uri = new Uri(currentUrl);
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                var random = new Random();

                var referers = new[]
                {
            $"https://www.google.com/search?q={Uri.EscapeDataString(uri.Host)}",
            $"https://www.bing.com/search?q={Uri.EscapeDataString(uri.Host)}",
            "https://duckduckgo.com/",
            baseUrl,
            $"{baseUrl}/",
            $"{baseUrl}/home",
            "" // No referer
        };

                return referers[random.Next(referers.Length)];
            }
            catch
            {
                return "";
            }
        }

        private int CalculateRetryDelay(int attemptNumber, Random random)
        {
            // Exponential backoff with jitter
            var baseDelay = Math.Min(1000 * (int)Math.Pow(1.5, attemptNumber), 10000);
            var jitter = random.Next(500, 2000);
            return baseDelay + jitter;
        }

        private string ClassifyHttpError(HttpRequestException ex)
        {
            var message = ex.Message.ToLower();

            if (message.Contains("403") || message.Contains("forbidden"))
                return "Blocked (403)";
            if (message.Contains("429") || message.Contains("too many"))
                return "Rate Limited (429)";
            if (message.Contains("401") || message.Contains("unauthorized"))
                return "Unauthorized (401)";
            if (message.Contains("404") || message.Contains("not found"))
                return "Not Found (404)";
            if (message.Contains("500") || message.Contains("internal server"))
                return "Server Error (500)";
            if (message.Contains("502") || message.Contains("bad gateway"))
                return "Bad Gateway (502)";
            if (message.Contains("503") || message.Contains("service unavailable"))
                return "Service Unavailable (503)";

            return "Network Error";
        }

        private void LogRequest(string url, string type, string status, string error, DateTime startTime)
        {
            OnRequestLog?.Invoke(new RequestLog
            {
                Url = url,
                Type = type,
                Status = status,
                Error = error,
                Timestamp = startTime,
                Duration = DateTime.Now - startTime
            });
        }

        private void UpdateProgress()
        {
            var total = _totalCities + _totalUniversities + _totalOffers + _totalApplications;
            var parsed = _parsedCities + _parsedUniversities + _parsedOffers + _parsedApplications;

            var percentage = total > 0 ? (parsed * 100.0 / total) : 0;

            OnProgressUpdate?.Invoke(new ProgressUpdate
            {
                TotalCities = _totalCities,
                ParsedCities = _parsedCities,
                TotalUniversities = _totalUniversities,
                ParsedUniversities = _parsedUniversities,
                TotalOffers = _totalOffers,
                ParsedOffers = _parsedOffers,
                TotalApplications = _totalApplications,
                ParsedApplications = _parsedApplications,
                OverallPercentage = percentage,
                CurrentStage = GetCurrentStage()
            });
        }

        private string GetCurrentStage()
        {
            if (_parsedCities < _totalCities) return "Parsing Cities";
            if (_parsedUniversities < _totalUniversities) return "Parsing Universities";
            if (_parsedOffers < _totalOffers) return "Parsing Offers";
            if (_parsedApplications < _totalApplications) return "Parsing Applications";
            return "Completed";
        }

        public async Task ParseAsync(CancellationToken cancellationToken = default)
        {
            // Calculate totals first for progress tracking
            await CalculateTotalsAsync(cancellationToken);
            UpdateProgress();

            await ParseCitiesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await ParseUniversitiesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await ParseOffersAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await ParseApplicationsAsync(cancellationToken);

            UpdateProgress();
        }

        private async Task CalculateTotalsAsync(CancellationToken cancellationToken)
        {
            // Get existing counts
            _parsedCities = await _db.Cities.CountAsync(cancellationToken);
            _parsedUniversities = await _db.Universities.CountAsync(cancellationToken);
            _parsedOffers = await _db.Offers.CountAsync(cancellationToken);
            _parsedApplications = await _db.Applications.Select(a => a.OfferId).Distinct().CountAsync(cancellationToken);

            // Estimate totals (you may need to adjust these based on actual data)
            if (_totalCities == 0)
            {
                var html = await GetHtmlAsync(_baseUrl, cancellationToken);
                var doc = await _context.OpenAsync(req => req.Content(html), cancellationToken);
                var rows = doc.QuerySelectorAll("body > div:nth-child(10) > div > div:nth-child(2) > div > div > table > tbody > tr");
                _totalCities = rows.Length;
            }

            // For other stages, we'll update totals as we discover them
            _totalUniversities = Math.Max(_parsedUniversities, 100); // Estimate
            _totalOffers = Math.Max(_parsedOffers, 500); // Estimate
            _totalApplications = Math.Max(_parsedApplications, 1000); // Estimate
        }

        // STEP 1 – Parse Cities
        private async Task ParseCitiesAsync(CancellationToken cancellationToken)
        {
            if (await _db.Cities.AnyAsync(cancellationToken))
            {
                _parsedCities = await _db.Cities.CountAsync(cancellationToken);
                _totalCities = _parsedCities;
                UpdateProgress();
                return; // already parsed
            }

            var html = await GetHtmlAsync(_baseUrl, cancellationToken);
            var doc = await _context.OpenAsync(req => req.Content(html), cancellationToken);

            var rows = doc.QuerySelectorAll("body > div:nth-child(10) > div > div:nth-child(2) > div > div > table > tbody > tr");
            _totalCities = rows.Length;
            UpdateProgress();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var a = row.QuerySelector("td:nth-child(1) > a");
                if (a == null) continue;

                var city = new City
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = a.TextContent.Trim(),
                    RequestParameter = a.GetAttribute("href")
                };

                _db.Cities.Add(city);
                _parsedCities++;
                UpdateProgress();
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        // STEP 2 – Parse Universities
        private async Task ParseUniversitiesAsync(CancellationToken cancellationToken)
        {
            var cities = await _db.Cities.ToListAsync(cancellationToken);
            var totalUnisEstimate = 0;

            foreach (var city in cities)
            {
                if (!city.Name.Equals("Київ")) continue;
                cancellationToken.ThrowIfCancellationRequested();

                if (await _db.Universities.AnyAsync(u => u.CityId == city.Id, cancellationToken))
                {
                    _parsedUniversities = await _db.Universities.CountAsync(u => u.CityId == city.Id, cancellationToken);
                    continue;
                }

                var html = await GetHtmlAsync(_baseUrl + city.RequestParameter, cancellationToken);
                var doc = await _context.OpenAsync(req => req.Content(html), cancellationToken);

                var links = doc.QuerySelectorAll("ul.section-search-result-list > li > a");
                totalUnisEstimate += links.Length;
                _totalUniversities = Math.Max(_totalUniversities, totalUnisEstimate + _parsedUniversities);
                UpdateProgress();

                foreach (var link in links)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var uni = new University
                    {
                        Id = Guid.NewGuid().ToString(),
                        CityId = city.Id,
                        Name = link.GetAttribute("title") ?? link.TextContent.Trim(),
                        RequestParameter = link.GetAttribute("href")
                    };

                    _db.Universities.Add(uni);
                    _parsedUniversities++;
                    UpdateProgress();
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // STEP 3 – Parse Offers
        private async Task ParseOffersAsync(CancellationToken cancellationToken)
        {
            var universities = await _db.Universities.ToListAsync(cancellationToken);
            var totalOffersEstimate = 0;

            foreach (var uni in universities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await _db.Offers.AnyAsync(o => o.UniversityId == uni.Id, cancellationToken))
                {
                    var count = await _db.Offers.CountAsync(o => o.UniversityId == uni.Id, cancellationToken);
                    _parsedOffers += count;
                    continue;
                }

                var html = await GetHtmlAsync(_baseUrl + uni.RequestParameter, cancellationToken);
                var doc = await _context.OpenAsync(req => req.Content(html), cancellationToken);

                var divs = doc.QuerySelectorAll("div.row.no-gutters.table-of-specs-item-row.qual2.base620.hidden")
                    .Where(d => d.QuerySelector("div:nth-child(1) > div.table-of-specs-item > b:nth-child(1)")?.TextContent == "Магістр");

                totalOffersEstimate += divs.Count();
                _totalOffers = Math.Max(_totalOffers, totalOffersEstimate + _parsedOffers);
                UpdateProgress();

                foreach (var div in divs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var specA = div.QuerySelector("div:nth-child(1) > div.table-of-specs-item > span > a");
                    var offerHref = div.QuerySelector("div.col-xl-2.col-lg-2.col-md-12 > div > a")?.GetAttribute("href");

                    if (specA == null || offerHref == null) continue;

                    var offer = new Offer
                    {
                        Id = Guid.NewGuid().ToString(),
                        UniversityId = uni.Id,
                        Speciality = specA.TextContent.Trim(),
                        RequestParameter = offerHref
                    };

                    _db.Offers.Add(offer);
                    _parsedOffers++;
                    UpdateProgress();
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // STEP 4 – Parse Applications
        private async Task ParseApplicationsAsync(CancellationToken cancellationToken)
        {
            var offers = await _db.Offers.ToListAsync(cancellationToken);
            _totalApplications = offers.Count;
            UpdateProgress();

            foreach (var offer in offers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await _db.Applications.AnyAsync(a => a.OfferId == offer.Id, cancellationToken))
                {
                    _parsedApplications++;
                    UpdateProgress();
                    continue;
                }

                if (string.IsNullOrEmpty(offer.RequestParameter))
                {
                    _parsedApplications++;
                    UpdateProgress();
                    continue;
                }

                var parts = offer.RequestParameter.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    _parsedApplications++;
                    UpdateProgress();
                    continue;
                }

                var y = parts[0].Replace("y", "");
                var uid = parts[2];
                var sid = parts[3];

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "action", "requests" },
                    { "y", y },
                    { "uid", uid },
                    { "sid", sid },
                    { "last", "10" }
                });

                var resp = await PostAsync("https://vstup.osvita.ua/api/", content, cancellationToken);
                var apiJson = await resp.Content.ReadFromJsonAsync<ApiResponse>(cancellationToken: cancellationToken);

                if (apiJson?.Url == null)
                {
                    _parsedApplications++;
                    UpdateProgress();
                    continue;
                }

                var rawJson = await GetJsonAsync<OsvitaJson>(apiJson.Url, cancellationToken);
                if (rawJson == null)
                {
                    _parsedApplications++;
                    UpdateProgress();
                    continue;
                }

                foreach (var r in rawJson.Requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var personName = r[4]?.ToString();
                    double grade = 0;

                    if (r[5] is JsonElement je && je.ValueKind == JsonValueKind.Number)
                        grade = je.GetDouble();

                    var person = new Person
                    {
                        Id = Guid.NewGuid().ToString(),
                        FullName = personName
                    };

                    // Use async version with cancellation token
                    if (!await _db.Persons.AnyAsync(p => p.FullName == personName, cancellationToken))
                    {
                        _db.Persons.Add(person);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        person = await _db.Persons.FirstAsync(p => p.FullName == personName, cancellationToken);
                    }

                    var app = new Application
                    {
                        Id = Guid.NewGuid().ToString(),
                        Grade = grade,
                        PersonId = person.Id,
                        OfferId = offer.Id,
                        RequestParameter = offer.RequestParameter
                    };

                    _db.Applications.Add(app);
                }

                await _db.SaveChangesAsync(cancellationToken);
                _parsedApplications++;
                UpdateProgress();
            }
        }

        // Helper DTOs
        private record ApiResponse(string Url);
        private record OsvitaJson(List<List<object>> Requests);
    }

    // Progress tracking models
    public class ProgressUpdate
    {
        public int TotalCities { get; set; }
        public int ParsedCities { get; set; }
        public int TotalUniversities { get; set; }
        public int ParsedUniversities { get; set; }
        public int TotalOffers { get; set; }
        public int ParsedOffers { get; set; }
        public int TotalApplications { get; set; }
        public int ParsedApplications { get; set; }
        public double OverallPercentage { get; set; }
        public string CurrentStage { get; set; } = "";
    }

    public class RequestLog
    {
        public string Url { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
    }
}