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
        private readonly HttpClient _http;

        public NominatimGeocodingService(HttpClient http) => _http = http;

        private static string NormalizeAddress(string address)
        {
            address = address.Trim();

            // If city already specified → do nothing
            if (address.Contains("Суми", StringComparison.OrdinalIgnoreCase) ||
                address.Contains("Sumy", StringComparison.OrdinalIgnoreCase))
            {
                return address;
            }

            // Add default city
            return $"Суми, {address}";
        }

        public async Task<(double lat, double lon)?> GeocodeAsync(string address, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            address = NormalizeAddress(address); // 👈 ADD THIS LINE

            if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
                _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SumyCRM", "1.0"));

            var url =
                "https://nominatim.openstreetmap.org/search" +
                "?format=jsonv2&limit=1&addressdetails=0" +
                "&countrycodes=ua" +                   // 👈 optional but recommended
                "&q=" + Uri.EscapeDataString(address);

            using var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Geocoding failed. {(int)resp.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement;
            if (arr.GetArrayLength() == 0) return null;

            var first = arr[0];

            if (double.TryParse(first.GetProperty("lat").GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(first.GetProperty("lon").GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lon))
            {
                return (lat, lon);
            }

            return null;
        }
    }
}
