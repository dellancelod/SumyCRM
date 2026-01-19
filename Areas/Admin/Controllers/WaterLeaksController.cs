using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;

namespace SumyCRM.Areas.Admin.Controllers
{

    [Area("Admin")]
    public class WaterLeaksController : Controller
    {
        private readonly DataManager _dataManager;
        private readonly IGeocodingService _geo;
        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpFactory;

        public WaterLeaksController(DataManager dataManager, IGeocodingService geo, IMemoryCache cache, IHttpClientFactory httpFactory)
        {
            _dataManager = dataManager;
            _geo = geo;
            _cache = cache;
            _httpFactory = httpFactory;
        }
        // Page with form + map
        public IActionResult Index() => View();

        // API: get all points
        [HttpGet]
        public async Task<IActionResult> Points(CancellationToken ct)
        {
            var points = await _dataManager.WaterLeakReports
                .GetWaterLeakReports()
                .OrderByDescending(x => x.DateAdded)
                .Select(x => new {
                    id = x.Id,
                    address = x.Address,
                    lat = x.Latitude,
                    lon = x.Longitude,
                    notes = x.Notes,
                    street = x.Street,
                    geometryJson = x.GeometryJson
                })
                .ToListAsync(ct);

            return Json(points);
        }

        public class CreateLeakDto
        {
            public string Address { get; set; } = "";
            public string? Notes { get; set; }
        }
        public class DeleteDto { public Guid Id { get; set; } }
        private static bool HasDigit(string s) => s.Any(char.IsDigit);
        private static string NormalizeStreetName(string s)
        {
            s = (s ?? "").Trim();

            // Collapse spaces
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }

        private static readonly string[] OverpassEndpoints = new[]
{
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.nchc.org.tw/api/interpreter"
};

        private async Task<List<List<(double lat, double lon)>>> GetStreetPolylinesAsync(string streetInput, CancellationToken ct)
        {
            var street = NormalizeStreetName(streetInput);
            if (string.IsNullOrWhiteSpace(street)) return new();

            var cacheKey = $"overpass:sumy:streetpoly:{street.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<List<(double lat, double lon)>>? cached) && cached != null && cached.Count > 0)
                return cached;

            // city center cached (ok)
            var city = await _cache.GetOrCreateAsync("geo:city:sumy", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30);
                entry.SlidingExpiration = TimeSpan.FromDays(7);
                return await _geo.GeocodeAsync("Суми, Україна", ct);
            });
            if (city == null) return new();

            var lat0 = city.Value.lat;
            var lon0 = city.Value.lon;
            var radius = 3500;

            var streetRegex = EscapeOverpassRegex(street);

            // 1) exact match first
            var qExact = BuildOverpassQuery(lat0, lon0, radius, $"^{streetRegex}$");
            // 2) fallback "contains" match
            var qLoose = BuildOverpassQuery(lat0, lon0, radius, streetRegex);

            var client = _httpFactory.CreateClient("overpass");

            // Try: exact (with retries + mirror fallback), then loose (with retries + mirror fallback)
            var lines = await ExecuteOverpassWithFallbackAsync(client, qExact, ct);
            if (lines.Count == 0)
                lines = await ExecuteOverpassWithFallbackAsync(client, qLoose, ct);

            // Cache: success long, empty very short (important)
            _cache.Set(cacheKey, lines, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lines.Count > 0 ? TimeSpan.FromDays(180) : TimeSpan.FromSeconds(45),
                SlidingExpiration = lines.Count > 0 ? TimeSpan.FromDays(14) : TimeSpan.FromSeconds(45)
            });

            return lines;

            string BuildOverpassQuery(double lat, double lon, int rad, string namePattern)
            {
                return $@"
[out:json][timeout:25];
(
  way(around:{rad},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)})
    [""highway""]
    [~""^(name|name:uk|name:ru)$""~""{namePattern}"",i];
);
out geom;";
            }
        }

        private async Task<List<List<(double lat, double lon)>>> ExecuteOverpassWithFallbackAsync(HttpClient client, string query, CancellationToken ct)
        {
            // Each endpoint: try 2 attempts (handles temporary overload)
            foreach (var endpoint in OverpassEndpoints)
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        using var resp = await client.PostAsync(endpoint,
                            new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query }), ct);

                        var json = await resp.Content.ReadAsStringAsync(ct);

                        // Retry only on transient statuses
                        if ((int)resp.StatusCode is 429 or 502 or 503 or 504)
                        {
                            await Task.Delay(250 * attempt, ct);
                            continue;
                        }

                        resp.EnsureSuccessStatusCode();

                        return ParseOverpassGeom(json);
                    }
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // timeout -> retry
                        await Task.Delay(250 * attempt, ct);
                    }
                    catch (HttpRequestException)
                    {
                        // network -> retry
                        await Task.Delay(250 * attempt, ct);
                    }
                }
            }

            return new();
        }

        private static List<List<(double lat, double lon)>> ParseOverpassGeom(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("elements", out var elements))
                return new();

            var lines = new List<List<(double lat, double lon)>>();

            foreach (var el in elements.EnumerateArray())
            {
                if (!el.TryGetProperty("geometry", out var geom)) continue;

                var coords = new List<(double lat, double lon)>();
                foreach (var p in geom.EnumerateArray())
                    coords.Add((p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble()));

                if (coords.Count >= 2)
                    lines.Add(coords);
            }

            return lines;
        }

        private static string EscapeOverpassRegex(string s)
        {
            // Escape regex special characters for Overpass ~ operator
            return System.Text.RegularExpressions.Regex.Escape(s.Trim());
        }


        // API: create point by address (server geocoding)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLeakDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Необхідно ввести адресу" });

            try
            {
                var input = dto.Address.Trim();

                // POINT (address with number)
                if (HasDigit(input))
                {
                    var geo = await _geo.GeocodeAsync(dto.Address, ct);
                    if (geo == null)
                        return BadRequest(new { error = "Адреси не знайдено. Перевірте правильний напис вулиці / номера будинку" });

                    var entity = new WaterLeakReport
                    {
                        Address = dto.Address.Trim(),
                        Latitude = geo.Value.lat,
                        Longitude = geo.Value.lon,
                        Notes = dto.Notes,
                        DateAdded = DateTime.UtcNow,
                        Status = "New",
                        Street = false
                    };

                    await _dataManager.WaterLeakReports.SaveWaterLeakReportAsync(entity);

                    return Ok(new
                    {
                        id = entity.Id,
                        address = entity.Address,
                        lat = entity.Latitude,
                        lon = entity.Longitude,
                        notes = entity.Notes
                    });
                }

                var lines = await GetStreetPolylinesAsync(input, ct);
                if (lines.Count == 0)
                    return BadRequest(new { error = $"Геометрія вулиці не знайдена для: '{dto.Address}'." });

                // center: use middle of the longest line
                var longest = lines.OrderByDescending(l => l.Count).First();
                var center = longest[longest.Count / 2];

                // GeometryJson now becomes array of lines: [ [[lat,lon],[lat,lon]], [[lat,lon],...] ]
                var geometryJson = System.Text.Json.JsonSerializer.Serialize(
                    lines.Select(line => line.Select(p => new[] { p.lat, p.lon }).ToList()).ToList()
                );

                var streetEntity = new WaterLeakReport
                {
                    Address = input,
                    Latitude = center.lat,
                    Longitude = center.lon,
                    Notes = dto.Notes,
                    DateAdded = DateTime.UtcNow,
                    Status = "New",
                    Street = true,
                    GeometryJson = geometryJson
                };

                await _dataManager.WaterLeakReports.SaveWaterLeakReportAsync(streetEntity);

                return Ok(new
                {
                    id = streetEntity.Id,
                    street = true,
                    address = streetEntity.Address,
                    lat = streetEntity.Latitude,
                    lon = streetEntity.Longitude,
                    notes = streetEntity.Notes,
                    geometryJson = streetEntity.GeometryJson
                });
            }
            catch (DbUpdateException ex)
            {
                // This is the one you previously hit: EF "See inner exception"
                return StatusCode(500, new
                {
                    error = "DB save failed",
                    inner = ex.InnerException?.Message,
                    detail = ex.ToString()
                });
            }
            catch (Exception ex)
            {
                // Geocoding errors etc.
                return StatusCode(500, new
                {
                    error = ex.Message,
                    detail = ex.ToString()
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] DeleteDto dto)
        {
            if (dto == null || dto.Id == Guid.Empty)
                return BadRequest(new { error = "Invalid id" });

            await _dataManager.WaterLeakReports.DeleteWaterLeakReportAsync(dto.Id);
            return Ok(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _dataManager.WaterLeakReports
                .GetWaterLeakReports()
                .ToListAsync();

            if (all.Count == 0)
                return Ok(new { ok = true, deleted = 0 });

            foreach (var item in all)
                await _dataManager.WaterLeakReports.DeleteWaterLeakReportAsync(item);

            return Ok(new { ok = true, deleted = all.Count });
        }
    }
}
