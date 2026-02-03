using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;
using SumyCRM.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SumyCRM.Services
{
    public interface ISumyStreetImportService
    {
        Task<int> RefreshAsync(CancellationToken ct);
    }

    public class SumyStreetImportService : ISumyStreetImportService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly AppDbContext _db;
        private readonly ILogger<SumyStreetImportService> _log;

        private static readonly string[] OverpassBases = new[]
        {
            "https://overpass-api.de/api/",
            "https://overpass.kumi.systems/api/",
            "https://overpass.openstreetmap.ru/api/"
        };

        // Cached in-memory per app run (you can store it in DB/settings if you want)
        private static long? _sumyAreaId;

        public SumyStreetImportService(
            IHttpClientFactory httpFactory,
            AppDbContext db,
            ILogger<SumyStreetImportService> log)
        {
            _httpFactory = httpFactory;
            _db = db;
            _log = log;
        }

        public async Task<int> RefreshAsync(CancellationToken ct)
        {
            var dbHasAny = await _db.Streets.AsNoTracking().AnyAsync(ct);

            var names = await DownloadSumyStreetNamesAsync(ct);

            if (names.Count == 0)
            {
                if (!dbHasAny)
                    _log.LogError("Sumy streets refresh FAILED: got 0 names and DB is empty.");
                else
                    _log.LogWarning("Sumy streets refresh: got 0 names (keeping old data).");

                return 0;
            }

            var incoming = names
                .Select(n => n.Trim())
                .Where(n => n.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .Select(n => new { Name = n, Norm = Normalize(n) })
                .Where(x => x.Norm.Length >= 2)
                .GroupBy(x => x.Norm)
                .Select(g => g.First())
                .ToList();

            var existing = await _db.Streets.ToDictionaryAsync(s => s.NameNorm, s => s, ct);
            var now = DateTime.UtcNow;

            foreach (var s in incoming)
            {
                if (existing.TryGetValue(s.Norm, out var row))
                {
                    if (!string.Equals(row.Name, s.Name, StringComparison.Ordinal))
                        row.Name = s.Name;

                    row.DateAdded = now;
                }
                else
                {
                    _db.Streets.Add(new Street
                    {
                        Name = s.Name,
                        NameNorm = s.Norm,
                        DateAdded = now
                    });
                }
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Sumy streets refresh OK. Incoming={Incoming}", incoming.Count);
            return incoming.Count;
        }

        private async Task<HashSet<string>> DownloadSumyStreetNamesAsync(CancellationToken ct)
        {
            // 1) Ensure we have a correct Overpass area id for Sumy
            if (_sumyAreaId == null)
            {
                _sumyAreaId = await ResolveSumyAreaIdViaNominatim(ct);
                if (_sumyAreaId == null)
                {
                    _log.LogError("Cannot resolve Sumy area id via Nominatim.");
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                _log.LogInformation("Resolved Sumy Overpass area id = {AreaId}", _sumyAreaId);
            }

            // 2) Query streets in that area
            var query = $"""
            [out:json][timeout:90];
            area({_sumyAreaId})->.a;
            way(area.a)["highway"]["name"];
            out tags;
            """;

            Exception? lastErr = null;

            foreach (var baseUrl in OverpassBases)
            {
                try
                {
                    using var client = _httpFactory.CreateClient("overpass");
                    client.BaseAddress = new Uri(baseUrl);
                    client.Timeout = TimeSpan.FromSeconds(110);

                    using var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("data", query)
                    });

                    using var req = new HttpRequestMessage(HttpMethod.Post, "interpreter")
                    {
                        Content = content
                    };

                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                    // Helpful debug for 400/other errors
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await SafeReadBodyAsync(resp, ct);
                        _log.LogWarning("Overpass non-success {Status} from {Base}. Body: {Body}",
                            (int)resp.StatusCode, baseUrl, body);
                    }

                    if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == HttpStatusCode.TooManyRequests)
                        continue;

                    if ((int)resp.StatusCode == 504 || resp.StatusCode == HttpStatusCode.GatewayTimeout)
                        continue;

                    resp.EnsureSuccessStatusCode();

                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                    var set = new HashSet<string>(StringComparer.Ordinal);

                    if (doc.RootElement.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in elements.EnumerateArray())
                        {
                            if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
                                continue;

                            if (!tags.TryGetProperty("name", out var nameProp))
                                continue;

                            var name = (nameProp.GetString() ?? "").Trim();
                            if (name.Length >= 2)
                                set.Add(name);
                        }
                    }

                    if (set.Count == 0)
                    {
                        _log.LogWarning("Overpass returned 200 but 0 streets from {Base}.", baseUrl);

                        // If area id somehow wrong, force re-resolve next time
                        // (rare, but protects you if nominatim gave wrong object)
                        // _sumyAreaId = null;

                        continue;
                    }

                    _log.LogInformation("Overpass OK from {Base}. Streets={Count}", baseUrl, set.Count);
                    return set;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    _log.LogWarning(ex, "Overpass failed for {Base}", baseUrl);
                }
            }

            _log.LogError(lastErr, "All Overpass instances failed or returned 0 streets.");
            return new HashSet<string>(StringComparer.Ordinal);
        }

        private async Task<long?> ResolveSumyAreaIdViaNominatim(CancellationToken ct)
        {
            // Nominatim returns OSM objects. For Overpass:
            // relation -> areaId = 3600000000 + relId
            // way      -> areaId = 2400000000 + wayId
            // (node areas generally not used here)

            var url = "https://nominatim.openstreetmap.org/search" +
                      "?format=jsonv2&limit=5&accept-language=uk" +
                      "&q=" + Uri.EscapeDataString("Суми, Україна");

            using var client = _httpFactory.CreateClient("Nominatim");
            client.Timeout = TimeSpan.FromSeconds(30);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("SumyCRM/1.0 (street-import)");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var osmType = item.TryGetProperty("osm_type", out var t) ? (t.GetString() ?? "") : "";
                var osmId = item.TryGetProperty("osm_id", out var id) ? id.GetInt64() : 0;

                // Try to prefer administrative boundary if present
                var category = item.TryGetProperty("category", out var cat) ? (cat.GetString() ?? "") : "";
                var type = item.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "") : "";

                if (osmId <= 0) continue;

                // Good hits are usually boundary/administrative relation
                if (osmType == "relation")
                {
                    // if looks admin-ish, accept immediately
                    if (category == "boundary" || type == "administrative" || type == "city")
                        return 3_600_000_000L + osmId;

                    // otherwise keep as fallback
                    return 3_600_000_000L + osmId;
                }

                if (osmType == "way")
                    return 2_400_000_000L + osmId;
            }

            return null;
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            try
            {
                var s = await resp.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(s)) return "(empty)";
                s = s.Trim();
                return s.Length > 600 ? s.Substring(0, 600) + "..." : s;
            }
            catch
            {
                return "(failed to read body)";
            }
        }

        private static string Normalize(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();

            var sb = new StringBuilder(s.Length);
            bool lastSpace = false;

            foreach (var ch in s)
            {
                var c = ch;
                if (c == '’' || c == '`') c = '\'';

                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                    continue;
                }

                if (c == '"' || c == '(' || c == ')' || c == ';')
                    continue;

                sb.Append(c);
                lastSpace = false;
            }

            return sb.ToString().Trim();
        }
    }
}
