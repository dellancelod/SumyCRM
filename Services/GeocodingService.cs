using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SumyCRM.Services
{
    public interface IGeocodingService
    {
        Task<(double lat, double lon, string shortAddress, string address)?> GeocodeAsync(string address, CancellationToken ct = default);
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

            if (address.Contains("місто Суми", StringComparison.OrdinalIgnoreCase) ||
                address.Contains("Sumy city", StringComparison.OrdinalIgnoreCase))
            {
                return address;
            }

            return $"Суми, {address}";
        }
        private static bool IsSumyCity(JsonElement addr)
        {
            // Nominatim може покласти населений пункт у різні ключі
            string[] keys = { "city", "town", "municipality" };

            foreach (var k in keys)
            {
                if (addr.TryGetProperty(k, out var el))
                {
                    var v = (el.GetString() ?? "").Trim();
                    if (v.Equals("Суми", StringComparison.OrdinalIgnoreCase) ||
                        v.Equals("Sumy", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
        public async Task<(double lat, double lon, string shortAddress, string address)?> GeocodeAsync(string address, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            address = NormalizeAddress(address);

            // ✅ CACHE KEY
            var cacheKey = $"geo:nominatim:{address.ToLowerInvariant()}";

            // ✅ Try cache first
            if (_cache.TryGetValue<(double lat, double lon, string displayName, string name)?>(cacheKey, out var cached))
                return cached;

            var http = _httpFactory.CreateClient("nominatim");

            var url =
                "search" +
                "?format=jsonv2&limit=10&addressdetails=1" +
                "&countrycodes=ua" +
                "&q=" + Uri.EscapeDataString(address);

            using var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Geocoding failed. {(int)resp.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement;
            (double lat, double lon, string displayName, string name)? result = null;

            if (arr.GetArrayLength() > 0)
            {
                for (int i = 0; i < arr.GetArrayLength(); i++)
                {
                    var first = arr[i];

                    if (!first.TryGetProperty("address", out var addrObj))
                        continue;

                    if (!IsSumyCity(addrObj))
                        continue;

                    bool hasHouse = addrObj.TryGetProperty("house_number", out var h) && !string.IsNullOrWhiteSpace(h.GetString());
                    if (!hasHouse) 
                        continue; // або result=null

                    string? house = null;
                    house = h.GetString();

                    if (double.TryParse(first.GetProperty("lat").GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(first.GetProperty("lon").GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lon))

                    {
                        string? name = null;
                        string? shortAddress = null;


                        if (first.TryGetProperty("name", out var nEl))
                            name = nEl.GetString();

                        if (first.TryGetProperty("address", out var addr))
                        {
                            string? road = null;

                            // possible street keys (priority order)
                            string[] roadKeys = { "road", "pedestrian", "residential", "highway" };

                            foreach (var k in roadKeys)
                            {
                                if (addr.TryGetProperty(k, out var r))
                                {
                                    road = r.GetString();
                                    if (!string.IsNullOrWhiteSpace(road))
                                        break;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(road))
                                shortAddress = house != null ? $"{house}, {road}" : name;
                        }

                        result = (lat, lon, shortAddress, name);
                    }
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
