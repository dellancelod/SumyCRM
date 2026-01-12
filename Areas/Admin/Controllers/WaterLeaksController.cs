using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
                .Select(x => new
                {
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

        private static bool HasDigit(string s) => (s ?? "").Any(char.IsDigit);

        private enum StreetKind { Unknown, Street, Avenue }

        /// <summary>
        /// Parses "вулиця/вул./улица/ул." and "проспект/просп./пр-т" and returns (kind, baseName)
        /// Example: "проспект Шевченка" => (Avenue, "Шевченка")
        ///          "вул. Шевченка"     => (Street, "Шевченка")
        ///          "Шевченка"         => (Unknown, "Шевченка")
        /// </summary>
        private static (StreetKind kind, string baseName) ParseStreet(string input)
        {
            var s = (input ?? "").Trim();

            bool hasStreet = Regex.IsMatch(s, @"\b(вулиця|вул|ул|улица)\b", RegexOptions.IgnoreCase);
            bool hasAvenue = Regex.IsMatch(s, @"\b(проспект|просп|пр-т|пр-т\.)\b", RegexOptions.IgnoreCase);

            var kind = hasAvenue ? StreetKind.Avenue : hasStreet ? StreetKind.Street : StreetKind.Unknown;

            // Remove type words from base name only
            s = Regex.Replace(s, @"\b(вулиця|вул\.|ул\.|улица|проспект|просп\.|пр-т|пр-т\.)\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return (kind, s);
        }

        private static string EscapeOverpassRegex(string s) => Regex.Escape((s ?? "").Trim());

        /// <summary>
        /// Queries Overpass for highway ways around Sumy and returns (name, coords) for each matched way.
        /// </summary>
        private async Task<List<(string name, List<(double lat, double lon)> coords)>> GetStreetWaysAsync(string streetInput, CancellationToken ct)
        {
            var (kind, baseName) = ParseStreet(streetInput);
            if (string.IsNullOrWhiteSpace(baseName)) return new();

            var city = await _geo.GeocodeAsync("Суми, Україна", ct);
            if (city == null) return new();

            var lat0 = city.Value.lat;
            var lon0 = city.Value.lon;

            var overpassUrl = "https://overpass-api.de/api/interpreter";
            var radius = 5500;

            var baseRegex = EscapeOverpassRegex(baseName);

            // If user specified type: try to match names that contain that type word.
            // If not specified: match anything containing base name and handle ambiguity later.
            string nameRegex = kind switch
            {
                StreetKind.Avenue => $@".*{baseRegex}.*", // filter later by "просп" keyword in tags
                StreetKind.Street => $@".*{baseRegex}.*", // filter later by "вул/вулиця" keyword in tags
                _ => $@".*{baseRegex}.*"
            };

            var q = $@"
                [out:json][timeout:40];
                (
                  way(around:{radius},{lat0.ToString(CultureInfo.InvariantCulture)},{lon0.ToString(CultureInfo.InvariantCulture)})
                    [""highway""]
                    [~""^(name|name:uk|name:ru)$""~""{nameRegex}"",i];
                );
                out tags geom;";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SumyCRM/1.0 (contact: admin@giftsbakery.com.ua)");

            using var resp = await client.PostAsync(
                overpassUrl,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = q }),
                ct
            );

            var json = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("elements", out var elements)) return new();

            var result = new List<(string name, List<(double lat, double lon)> coords)>();

            foreach (var el in elements.EnumerateArray())
            {
                if (!el.TryGetProperty("geometry", out var geom)) continue;

                string name = "";
                if (el.TryGetProperty("tags", out var tags))
                {
                    if (tags.TryGetProperty("name:uk", out var nUk)) name = nUk.GetString() ?? "";
                    else if (tags.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                    else if (tags.TryGetProperty("name:ru", out var nRu)) name = nRu.GetString() ?? "";
                }

                // Only keep things that actually contain baseName (extra safety for broad regex)
                if (!string.IsNullOrWhiteSpace(name) &&
                    name.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var coords = new List<(double lat, double lon)>();
                foreach (var p in geom.EnumerateArray())
                    coords.Add((p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble()));

                if (coords.Count >= 2)
                    result.Add((name, coords));
            }

            return result;
        }

        private static bool LooksLikeAvenueName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // OSM often has "проспект ..." or "просп. ..."
            return name.Contains("просп", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("пр-т", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeStreetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // OSM often has "вулиця ..." or "вул. ..."
            return name.Contains("вул", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("вулиц", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("улиц", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("ул.", StringComparison.OrdinalIgnoreCase);
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

                // =========================
                // POINT (address with number)
                // =========================
                if (HasDigit(input))
                {
                    var geo = await _geo.GeocodeAsync(input, ct);
                    if (geo == null)
                        return BadRequest(new { error = "Адреси не знайдено. Перевірте правильний напис вулиці / номера будинку" });

                    var entity = new WaterLeakReport
                    {
                        Address = input,
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

                // =========================
                // STREET (no number): draw geometry
                // =========================
                var ways = await GetStreetWaysAsync(input, ct);
                if (ways.Count == 0)
                    return BadRequest(new { error = $"Геометрія вулиці не знайдена для: '{input}'." });

                var (kind, baseName) = ParseStreet(input);

                // Distinct names (options) for UI
                var uniqueNames = ways.Select(x => x.name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // If user didn't specify kind and we see multiple different names that match (e.g. "проспект Шевченка" and "вулиця Шевченка")
                if (kind == StreetKind.Unknown && uniqueNames.Count > 1)
                {
                    // Prioritize likely "просп" / "вул" in options (cleaner for user)
                    var ordered = uniqueNames
                        .OrderByDescending(n => LooksLikeAvenueName(n) || LooksLikeStreetName(n))
                        .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return Conflict(new
                    {
                        error = "Знайдено кілька варіантів. Уточніть тип:",
                        options = ordered
                    });
                }

                // Filter ways by kind if specified
                var filtered = ways;
                if (kind == StreetKind.Avenue)
                {
                    var f = ways.Where(w => LooksLikeAvenueName(w.name)).ToList();
                    if (f.Count > 0) filtered = f;
                }
                else if (kind == StreetKind.Street)
                {
                    var f = ways.Where(w => LooksLikeStreetName(w.name)).ToList();
                    if (f.Count > 0) filtered = f;
                }

                var lines = filtered.Select(x => x.coords).ToList();

                // Use the "best" display name (if Overpass provided one)
                var resolvedName = filtered
                    .Select(x => x.name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? input;

                // Center: use middle of the longest line
                var longest = lines.OrderByDescending(l => l.Count).First();
                var center = longest[longest.Count / 2];

                // GeometryJson: array of lines: [ [[lat,lon],[lat,lon]], [[lat,lon],...] ]
                var geometryJson = JsonSerializer.Serialize(
                    lines.Select(line => line.Select(p => new[] { p.lat, p.lon }).ToList()).ToList()
                );

                var streetEntity = new WaterLeakReport
                {
                    Address = resolvedName,              // <-- store resolved full name if possible
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
                return StatusCode(500, new
                {
                    error = "DB save failed",
                    inner = ex.InnerException?.Message,
                    detail = ex.ToString()
                });
            }
            catch (Exception ex)
            {
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
