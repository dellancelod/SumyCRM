using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        // ✅ Remove noisy tokens that often break Nominatim matching (e.g. "будинок 12")
        //    Also tries to keep a clean "street + number" pattern.
        private static string NormalizeQueryForNominatim(string input)
        {
            var s = (input ?? "").Trim();

            if (s.Length == 0) return s;

            // unify common separators, remove quotes
            s = s.Replace("“", "\"").Replace("”", "\"").Replace("’", "'").Replace("`", "'");

            // Convert "будинок 12" / "д. 12" / "house 12" -> "12"
            // (do this BEFORE removing the words, so we don't accidentally leave double spaces)
            s = Regex.Replace(
                s,
                @"\b(буд(инок|\.?)|дім|д\.|house|home|bldg|building)\s*([0-9]+[A-Za-zА-Яа-яІіЇїЄєҐґ\-\/]*)\b",
                "$3",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Remove other address-noise words (keep "вул/вулиця" if you want — Nominatim understands it, but it's optional)
            s = Regex.Replace(
                s,
                @"\b(буд(инок|\.?)|дім|д\.|кв(артира|\.?)|кв\.|під'?їзд|під'?їзд\.|під'?їзду|поверх|офіс|оф\.|корп(ус|\.?)|к(орп|\/корп)\.?|парадн(е|а)|пров(улок|\.?)|пл(оща|\.?)|район)\b",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // collapse punctuation to spaces (keep comma because it helps)
            s = Regex.Replace(s, @"[;:\(\)\[\]\{\}]+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            // Optional: if user wrote "Харківська вулиця 12" -> "вулиця Харківська 12" (better for UA)
            // Only apply if it looks like "<word> (вулиця|вул) <number>"
            s = Regex.Replace(
                s,
                @"^(?<name>[\p{L}\-'\. ]+)\s+(вулиця|вул\.?)\s+(?<num>[0-9]+[A-Za-zА-Яа-яІіЇїЄєҐґ\-\/]*)$",
                "вулиця ${name} ${num}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

            return s;
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

            // ✅ sanitize user input first (fixes "будинок 12" etc.)
            var cleaned = NormalizeQueryForNominatim(address);

            // then apply your "Суми, ..." normalizer
            cleaned = NormalizeAddress(cleaned);

            // ✅ CACHE KEY (use cleaned form!)
            var cacheKey = $"geo:nominatim:{cleaned.ToLowerInvariant()}";

            if (_cache.TryGetValue<(double lat, double lon, string shortAddress, string address)?>(cacheKey, out var cached))
                return cached;

            var http = _httpFactory.CreateClient("nominatim");

            var url =
                "search" +
                "?format=jsonv2&limit=5&addressdetails=1" +
                "&countrycodes=ua" +
                "&q=" + Uri.EscapeDataString(cleaned);

            using var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Geocoding failed. {(int)resp.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement;

            (double lat, double lon, string shortAddress, string address)? result = null;

            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                for (int i = 0; i < arr.GetArrayLength(); i++)
                {
                    var item = arr[i];

                    if (!item.TryGetProperty("address", out var addrObj))
                        continue;

                    if (!IsSumyCity(addrObj))
                        continue;

                    if (!double.TryParse(item.GetProperty("lat").GetString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
                        !double.TryParse(item.GetProperty("lon").GetString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var lon))
                    {
                        continue;
                    }

                    string? displayName = null;
                    if (item.TryGetProperty("display_name", out var dnEl))
                        displayName = dnEl.GetString();

                    // Build a short address like "12, вулиця Харківська"
                    string? shortAddress = null;
                    if (item.TryGetProperty("address", out var addr))
                    {
                        string? house = null;
                        string? road = null;

                        if (addr.TryGetProperty("house_number", out var h))
                            house = h.GetString();

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
                            shortAddress = !string.IsNullOrWhiteSpace(house) ? $"{house}, {road}" : road;
                    }

                    // Fallback: if we couldn't compose from address parts
                    shortAddress ??= displayName;

                    result = (lat, lon, shortAddress ?? "", displayName ?? "");
                    break; // ✅ first good Sumy match is enough
                }
            }

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = result != null ? TimeSpan.FromDays(90) : TimeSpan.FromHours(6),
                SlidingExpiration = TimeSpan.FromDays(7)
            });

            return result;
        }
    }
}
