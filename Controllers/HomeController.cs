using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;

namespace SumyCRM.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db)
        {
            _db = db;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            ViewBag.Title = "Карта подій";
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetMapEvents()
        {
            var items = await _db.Events
                .AsNoTracking()
                .Where(x => x.Latitude != null && x.Longitude != null)
                .OrderByDescending(x => x.DateAdded)
                .Select(x => new
                {
                    id = x.Id,
                    requestId = x.RequestId,
                    requestNumber = x.RequestNumber,
                    categoryName = x.CategoryName,
                    streetName = x.StreetName,
                    address = x.Address,
                    text = x.Text,
                    lat = x.Latitude,
                    lon = x.Longitude,
                    isCompleted = x.IsCompleted,

                    // ЯВНО UTC ISO
                    dateAdded = x.DateAdded.Kind == DateTimeKind.Utc
                        ? x.DateAdded.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        : DateTime.SpecifyKind(x.DateAdded, DateTimeKind.Utc)
                            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                })
                .ToListAsync();

            return Json(items);
        }
    }
}