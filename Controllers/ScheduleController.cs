using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;

namespace SumyCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScheduleController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ScheduleController> _logger;

        public ScheduleController(AppDbContext db, ILogger<ScheduleController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/schedule?route=12
        /// Если найдено — вернёт имя аудиофайла (plain text),
        /// если нет — "NOT_FOUND"
        /// </summary>
        [HttpGet]
        public IActionResult Get([FromQuery] string route)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                return BadRequest("route is required");
            }

            route = route.Trim();

            _logger.LogInformation("Schedule request for route {Route}", route);

            var schedule = _db.Schedules
                .AsNoTracking()
                .FirstOrDefault(r => r.Number == route);

            if (schedule == null)
            {
                _logger.LogInformation("Schedule NOT_FOUND for route {Route}", route);
                // Asterisk ждёт строку "NOT_FOUND"
                return Content("NOT_FOUND", "text/plain");
            }

            _logger.LogInformation("Schedule found for route {Route}: {AudioFileName}",
                route, schedule.AudioFileName);

            // Просто имя файла без .wav — Asterisk сделает Playback(${SCHED_RESPONSE})
            return Content(schedule.AudioFileName, "text/plain");
        }
    }
}
