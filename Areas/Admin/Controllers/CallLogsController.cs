using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Controllers
{

    [Area("Admin")]
    public class CallLogsController : Controller
    {
        private readonly DataManager _dataManager;

        public CallLogsController(DataManager dataManager)
        {
            _dataManager = dataManager;
        }

        // GET: /CallLogs
        // Shows grouped (cascade) view by CallId
        [HttpGet("/calls/logs")]
        public async Task<IActionResult> Index(
            [FromQuery] string? q,
            [FromQuery] int days = 7,
            [FromQuery] int maxCalls = 200,
            CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 90);
            maxCalls = Math.Clamp(maxCalls, 20, 2000);

            var sinceUtc = DateTime.UtcNow.AddDays(-days);

            var baseQuery = _dataManager.CallEvents.GetCallEvents()
                .Where(x => x.DateAdded >= sinceUtc);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                baseQuery = baseQuery.Where(x =>
                    (x.CallId != null && x.CallId.Contains(q)) ||
                    (x.Caller != null && x.Caller.Contains(q)) ||
                    x.Event.Contains(q) ||
                    (x.Data != null && x.Data.Contains(q)));
            }

            // Take latest calls first (by last event time)
            var callHeads = await baseQuery
                .GroupBy(x => x.CallId ?? "")
                .Select(g => new
                {
                    CallId = g.Key,
                    LastAt = g.Max(x => x.DateAdded)
                })
                .OrderByDescending(x => x.LastAt)
                .Take(maxCalls)
                .ToListAsync(ct);

            var callIds = callHeads.Select(x => x.CallId).ToList();

            var events = await baseQuery
                .Where(x => callIds.Contains(x.CallId ?? ""))
                .OrderBy(x => x.DateAdded)
                .ToListAsync(ct);

            var groups = events
                .GroupBy(x => x.CallId ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            var vm = new CallLogsCascadeViewModel
            {
                Days = days,
                Query = q,
                Calls = callHeads.Select(h =>
                {
                    groups.TryGetValue(h.CallId, out var evs);
                    evs ??= new List<CallEvent>();

                    var first = evs.FirstOrDefault();
                    var last = evs.LastOrDefault();

                    return new CallLogsCascadeCall
                    {
                        CallId = h.CallId,
                        Caller = first?.Caller ?? "",
                        FirstAtUtc = first?.DateAdded ?? h.LastAt,
                        LastAtUtc = last?.DateAdded ?? h.LastAt,
                        Events = evs.Select(e => new CallLogsCascadeEvent
                        {
                            AtUtc = e.DateAdded,
                            Event = e.Event,
                            Data = e.Data ?? ""
                        }).ToList()
                    };
                }).ToList()
            };

            return View("Index", vm);
        }
    }

    // ===== ViewModels (keep here or move to separate folder) =====
    public class CallLogsCascadeViewModel
    {
        public int Days { get; set; }
        public string? Query { get; set; }
        public List<CallLogsCascadeCall> Calls { get; set; } = new();
    }

    public class CallLogsCascadeCall
    {
        public string CallId { get; set; } = "";
        public string Caller { get; set; } = "";
        public DateTime FirstAtUtc { get; set; }
        public DateTime LastAtUtc { get; set; }
        public List<CallLogsCascadeEvent> Events { get; set; } = new();
    }

    public class CallLogsCascadeEvent
    {
        public DateTime AtUtc { get; set; }
        public string Event { get; set; } = "";
        public string Data { get; set; } = "";
    }
}
