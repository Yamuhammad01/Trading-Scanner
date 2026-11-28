using System.Net;

namespace TradingScanner.Infrastructure.GateIo;

internal static class GateIoHttpRetryHelper
{
    private const int MaxRetries = 3;

    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await sendAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var statusCode = (int)response.StatusCode;
                var isTransient = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                  statusCode >= 500;

                if (!isTransient || attempt >= MaxRetries)
                {
                    return response;
                }

                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                response = null;

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                response?.Dispose();

                if (attempt >= MaxRetries)
                {
                    throw;
                }

                var delay = GetRetryBackoff(attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is TimeoutException ||
               ex.InnerException is HttpRequestException ||
               ex.InnerException is TaskCanceledException ||
               ex.InnerException is TimeoutException;
    }

    private static TimeSpan GetRetryBackoff(int attempt)
    {
        var backoffMs = 500 * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero)
            {
                return diff > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : diff;
            }
        }

        return GetRetryBackoff(attempt);
    }
}
