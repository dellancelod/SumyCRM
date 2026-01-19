using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SumyCRM.Services
{
    public interface IGeocodingService
    {
        Task<(double lat, double lon)?> GeocodeAsync(string address, CancellationToken ct = default);
    }


    public class NominatimGeocodingService : IGeocodingService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IMemoryCache _cache;

        public NominatimGeocodingService(IHttpClientFactory httpFactory, IMemoryCache cache)
        {
            _httpFactory = httpFactory;
            _cache = cache;
        }

        private static string NormalizeAddress(string address)
        {
            address = (address ?? "").Trim();

            if (address.Contains("Суми", StringComparison.OrdinalIgnoreCase) ||
                address.Contains("Sumy", StringComparison.OrdinalIgnoreCase))
            {
                return address;
            }

            return $"Суми, {address}";
        }

        public async Task<(double lat, double lon)?> GeocodeAsync(string address, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            address = NormalizeAddress(address);

            // ✅ CACHE KEY
            var cacheKey = $"geo:nominatim:{address.ToLowerInvariant()}";

            // ✅ Try cache first
            if (_cache.TryGetValue<(double lat, double lon)?>(cacheKey, out var cached))
                return cached;

            var http = _httpFactory.CreateClient("nominatim");

            var url =
                "search" +
                "?format=jsonv2&limit=1&addressdetails=0" +
                "&countrycodes=ua" +
                "&q=" + Uri.EscapeDataString(address);

            using var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Geocoding failed. {(int)resp.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement;
            (double lat, double lon)? result = null;

            if (arr.GetArrayLength() > 0)
            {
                var first = arr[0];

                if (double.TryParse(first.GetProperty("lat").GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(first.GetProperty("lon").GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    result = (lat, lon);
                }
            }

            // ✅ Cache both success and "null" to stop repeated slow calls for bad input
            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = result != null ? TimeSpan.FromDays(90) : TimeSpan.FromHours(6),
                SlidingExpiration = TimeSpan.FromDays(7)
            });

            return result;
        }
    }
}
