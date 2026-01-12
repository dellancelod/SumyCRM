using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public WaterLeaksController(DataManager dataManager, IGeocodingService geo)
        {
            _dataManager = dataManager;
            _geo = geo;
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

        private async Task<List<List<(double lat, double lon)>>> GetStreetPolylinesAsync(string streetInput, CancellationToken ct)
        {
            var street = NormalizeStreetName(streetInput);
            if (string.IsNullOrWhiteSpace(street)) return new();

            var city = await _geo.GeocodeAsync("Суми, Україна", ct);
            if (city == null) return new();

            var lat0 = city.Value.lat;
            var lon0 = city.Value.lon;

            var overpassUrl = "https://overpass-api.de/api/interpreter";
            var radius = 7000; // <-- increase a bit for full city coverage
            var streetRegex = EscapeOverpassRegex(street);

            // Search in multiple tags: name / name:uk / name:ru
            var q = $@"
                [out:json][timeout:40];
                (
                  way(around:{radius},{lat0.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon0.ToString(System.Globalization.CultureInfo.InvariantCulture)})
                    [""highway""]
                    [~""^(name|name:uk|name:ru)$""~""{streetRegex}"",i];
                );
                out geom;";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SumyCRM/1.0 (contact: admin@giftsbakery.com.ua)");

            using var resp = await client.PostAsync(overpassUrl,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = q }), ct);

            var json = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var elements = doc.RootElement.GetProperty("elements");

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
