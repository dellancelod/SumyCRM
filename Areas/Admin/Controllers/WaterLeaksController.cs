using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class WaterLeaksController : Controller
    {
        private readonly DataManager _dataManager;
        private readonly IGeocodingService _geo;
        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpFactory;
        private readonly AppDbContext _db;

        public WaterLeaksController(
            DataManager dataManager,
            IGeocodingService geo,
            IMemoryCache cache,
            IHttpClientFactory httpFactory,
            AppDbContext db)
        {
            _dataManager = dataManager;
            _geo = geo;
            _cache = cache;
            _httpFactory = httpFactory;
            _db = db;
        }

        public IActionResult Index() => View();

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

        public class DeleteDto
        {
            public Guid Id { get; set; }
        }

        private static bool HasDigit(string s) => s.Any(char.IsDigit);

        private static string NormalizeStreetName(string s)
        {
            s = (s ?? "").Trim();
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static bool TryParseHouseRange(string input, out string streetPart, out int from, out int to)
        {
            streetPart = "";
            from = to = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            var s = NormalizeStreetName(input);

            var m = Regex.Match(
                s,
                @"^(?<street>.*?)[,\s]+(?<a>\d{1,5})\s*[-–]\s*(?<b>\d{1,5})\s*$",
                RegexOptions.CultureInvariant);

            if (!m.Success)
                return false;

            streetPart = NormalizeStreetName(m.Groups["street"].Value);
            if (string.IsNullOrWhiteSpace(streetPart))
                return false;

            if (!int.TryParse(m.Groups["a"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                return false;
            if (!int.TryParse(m.Groups["b"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                return false;

            if (a <= 0 || b <= 0)
                return false;

            from = Math.Min(a, b);
            to = Math.Max(a, b);

            if (to - from > 200)
                return false;

            return true;
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

            var qExact = BuildOverpassQuery(lat0, lon0, radius, $"^{streetRegex}$");
            var qLoose = BuildOverpassQuery(lat0, lon0, radius, streetRegex);

            var client = _httpFactory.CreateClient("overpass");

            var lines = await ExecuteOverpassWithFallbackAsync(client, qExact, ct);
            if (lines.Count == 0)
                lines = await ExecuteOverpassWithFallbackAsync(client, qLoose, ct);

            _cache.Set(cacheKey, lines, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lines.Count > 0 ? TimeSpan.FromDays(180) : TimeSpan.FromSeconds(45),
                SlidingExpiration = lines.Count > 0 ? TimeSpan.FromDays(14) : TimeSpan.FromSeconds(45)
            });

            return lines;

            static string BuildOverpassQuery(double lat, double lon, int rad, string namePattern)
            {
                return $@"
                [out:json][timeout:25];
                (
                  way(around:{rad},{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)})
                    [""highway""]
                    [~""^(name|name:uk|name:ru)$""~""{namePattern}"",i];
                );
                out geom;";
            }
        }

        private async Task<List<List<(double lat, double lon)>>> ExecuteOverpassWithFallbackAsync(HttpClient client, string query, CancellationToken ct)
        {
            foreach (var endpoint in OverpassEndpoints)
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        using var resp = await client.PostAsync(
                            endpoint,
                            new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query }),
                            ct);

                        var json = await resp.Content.ReadAsStringAsync(ct);

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
                        await Task.Delay(250 * attempt, ct);
                    }
                    catch (HttpRequestException)
                    {
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
            return Regex.Escape(s.Trim());
        }

        private static string ExtractStreetName(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return "";

            var s = address.Trim();
            s = Regex.Replace(s, @"\s+", " ");

            var patterns = new[]
            {
                @"(?i)\b(вул\.?|вулиця)\s+([^\.,\d]+)",
                @"(?i)\b(просп\.?|проспект)\s+([^\.,\d]+)",
                @"(?i)\b(пров\.?|провулок)\s+([^\.,\d]+)",
                @"(?i)\b(пл\.?|площа)\s+([^\.,\d]+)",
                @"(?i)\b(майдан)\s+([^\.,\d]+)",
                @"(?i)\b(наб\.?|набережна)\s+([^\.,\d]+)",
                @"(?i)\b(шосе)\s+([^\.,\d]+)",
                @"(?i)\b(бульв\.?|бульвар)\s+([^\.,\d]+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(s, pattern, RegexOptions.CultureInvariant);
                if (match.Success)
                    return $"{match.Groups[1].Value.Trim().TrimEnd('.')} {match.Groups[2].Value.Trim()}".Trim();
            }

            var firstPart = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .FirstOrDefault();

            return firstPart ?? "";
        }

        private async Task CreateOrUpdateWaterLeakEventAsync(WaterLeakReport leak, CancellationToken ct)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.WaterLeakReportId == leak.Id, ct);

            if (ev == null)
            {
                ev = new Event
                {
                    WaterLeakReportId = leak.Id,
                    SourceType = "WaterLeak",
                    DateAdded = leak.DateAdded
                };
                _db.Events.Add(ev);
            }

            ev.RequestId = null;
            ev.SourceType = "WaterLeak";
            ev.CategoryName = "Водопостачання";
            ev.StreetName = ExtractStreetName(leak.Address);
            ev.Address = leak.Address ?? "";
            ev.Text = leak.Notes ?? "Відключення води";
            ev.Latitude = leak.Latitude;
            ev.Longitude = leak.Longitude;
            ev.IsCompleted = false;
            ev.DateAdded = leak.DateAdded;

            await _db.SaveChangesAsync(ct);
        }

        private async Task DeleteWaterLeakEventAsync(Guid waterLeakId, CancellationToken ct)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.WaterLeakReportId == waterLeakId, ct);
            if (ev != null)
            {
                _db.Events.Remove(ev);
                await _db.SaveChangesAsync(ct);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLeakDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Address))
                return BadRequest(new { error = "Необхідно ввести адресу" });

            try
            {
                var input = dto.Address.Trim();

                if (TryParseHouseRange(input, out var streetPart, out var from, out var to))
                {
                    const int maxPoints = 60;
                    var count = to - from + 1;
                    if (count > maxPoints)
                        return BadRequest(new { error = $"Занадто великий діапазон ({count}). Максимум: {maxPoints} адрес за раз." });

                    var created = new List<object>();

                    for (int n = from; n <= to; n++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var addr = $"{streetPart} {n}";
                        var geo = await _geo.GeocodeAsync(addr, ct);
                        if (geo == null)
                            continue;

                        var finalAddress = geo.Value.shortAddress.Any(char.IsDigit)
                            ? geo.Value.shortAddress
                            : addr;

                        var entity = new WaterLeakReport
                        {
                            Address = finalAddress,
                            Latitude = geo.Value.lat,
                            Longitude = geo.Value.lon,
                            Notes = dto.Notes,
                            DateAdded = DateTime.UtcNow,
                            Status = "New",
                            Street = false
                        };

                        await _dataManager.WaterLeakReports.SaveWaterLeakReportAsync(entity);
                        await CreateOrUpdateWaterLeakEventAsync(entity, ct);

                        created.Add(new
                        {
                            id = entity.Id,
                            address = entity.Address,
                            lat = entity.Latitude,
                            lon = entity.Longitude,
                            notes = entity.Notes
                        });
                    }

                    if (created.Count == 0)
                        return BadRequest(new { error = $"Не вдалося знайти жодної адреси у діапазоні: {streetPart} {from}-{to}." });

                    return Ok(new
                    {
                        range = true,
                        requested = new { street = streetPart, from, to },
                        created = created.Count,
                        points = created
                    });
                }

                if (HasDigit(input))
                {
                    var geo = await _geo.GeocodeAsync(dto.Address, ct);
                    if (geo == null)
                        return BadRequest(new { error = "Адреси не знайдено. Перевірте правильний напис вулиці / номера будинку" });

                    var finalAddress = geo.Value.shortAddress.Any(char.IsDigit)
                        ? geo.Value.shortAddress
                        : input;

                    var entity = new WaterLeakReport
                    {
                        Address = finalAddress,
                        Latitude = geo.Value.lat,
                        Longitude = geo.Value.lon,
                        Notes = dto.Notes,
                        DateAdded = DateTime.UtcNow,
                        Status = "New",
                        Street = false
                    };

                    await _dataManager.WaterLeakReports.SaveWaterLeakReportAsync(entity);
                    await CreateOrUpdateWaterLeakEventAsync(entity, ct);

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

                var longest = lines.OrderByDescending(l => l.Count).First();
                var center = longest[longest.Count / 2];

                var geometryJson = System.Text.Json.JsonSerializer.Serialize(
                    lines.Select(line => line.Select(p => new[] { p.lat, p.lon }).ToList()).ToList()
                );

                var geoStreet = await _geo.GeocodeAsync(input, ct);
                var officialStreet = geoStreet?.address;
                if (string.IsNullOrWhiteSpace(officialStreet))
                    officialStreet = input;

                var streetEntity = new WaterLeakReport
                {
                    Address = officialStreet,
                    Latitude = center.lat,
                    Longitude = center.lon,
                    Notes = dto.Notes,
                    DateAdded = DateTime.UtcNow,
                    Status = "New",
                    Street = true,
                    GeometryJson = geometryJson
                };

                await _dataManager.WaterLeakReports.SaveWaterLeakReportAsync(streetEntity);
                await CreateOrUpdateWaterLeakEventAsync(streetEntity, ct);

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

            await DeleteWaterLeakEventAsync(dto.Id, HttpContext.RequestAborted);
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
            {
                await DeleteWaterLeakEventAsync(item.Id, HttpContext.RequestAborted);
                await _dataManager.WaterLeakReports.DeleteWaterLeakReportAsync(item);
            }

            return Ok(new { ok = true, deleted = all.Count });
        }
    }
}