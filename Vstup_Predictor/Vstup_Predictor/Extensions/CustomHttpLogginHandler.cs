using System.Diagnostics;

namespace Vstup_Predictor.Extensions
{
    public class CustomHttpLoggingHandler : DelegatingHandler
    {
        private readonly ILogger<CustomHttpLoggingHandler> _logger;

        public CustomHttpLoggingHandler(ILogger<CustomHttpLoggingHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                //stopwatch.Stop();

                //_logger.LogInformation("HTTP {Method} {Url} - {StatusCode} ({Duration}ms)",
                //    request.Method,
                //    request.RequestUri,
                //    (int)response.StatusCode,
                //    stopwatch.ElapsedMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError("HTTP {Method} {Url} - Failed after {Duration}ms: {Error}",
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds,
                    ex.Message);
                throw;
            }
        }
    }
}
