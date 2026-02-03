using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data; // <- your namespace
using System.Text;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("admin/addresses")]
    public class AddressesController : Controller
    {
        private readonly AppDbContext _db;

        public AddressesController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("suggest")]
        public async Task<IActionResult> Suggest([FromQuery] string term, CancellationToken ct)
        {
            term ??= "";
            term = term.Trim();

            if (term.Length < 2)
                return Json(Array.Empty<string>());

            // If user types "лушп, 12" - search only before comma
            var raw = term.Split(',')[0].Trim();
            if (raw.Length < 2)
                return Json(Array.Empty<string>());

            var norm = StreetNormalize(raw);

            // 1) First take prefix matches (fast + "feels right")
            var prefix = await _db.Streets
                .AsNoTracking()
                .Where(s => s.NameNorm.StartsWith(norm))
                .OrderBy(s => s.Name)
                .Select(s => s.Name)
                .Take(12)
                .ToListAsync(ct);

            // If already enough, return
            if (prefix.Count >= 12)
                return Json(prefix);

            // 2) Add substring matches, excluding ones already returned
            // NOTE: Contains may be slower on very large tables, but still OK for city streets.
            var need = 12 - prefix.Count;

            var more = await _db.Streets
                .AsNoTracking()
                .Where(s => !s.NameNorm.StartsWith(norm) && s.NameNorm.Contains(norm))
                .OrderBy(s => s.Name)
                .Select(s => s.Name)
                .Take(need)
                .ToListAsync(ct);

            prefix.AddRange(more);
            return Json(prefix);
        }

        private static string StreetNormalize(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();

            var sb = new System.Text.StringBuilder(s.Length);
            bool lastSpace = false;

            foreach (var ch in s)
            {
                var c = ch;

                // unify apostrophes
                if (c == '’' || c == '`') c = '\'';

                // normalize whitespace
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                    continue;
                }

                // drop some punctuation
                if (c == '"' || c == '(' || c == ')' || c == ';')
                    continue;

                sb.Append(c);
                lastSpace = false;
            }

            return sb.ToString().Trim();
        }

    }
}
