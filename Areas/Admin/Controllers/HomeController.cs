using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Audio;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db, IConfiguration config)
        {
            _db = db;
        }

        // Show all requests
        public IActionResult Index()
        {
            var list = _db.Requests.OrderByDescending(r => r.CreatedAt).ToList();
            return View(list);
        }

        // Upload page
        public IActionResult Upload()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await _db.Requests.FindAsync(id);
            if (record == null)
                return NotFound();

            // Try to delete audio file
            if (!string.IsNullOrWhiteSpace(record.AudioFilePath))
            {
                // AudioFilePath like "/audio/xxx.wav"
                var relativePath = record.AudioFilePath.TrimStart('/', '\\');
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);

                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    catch
                    {
                        // ignore file delete errors, DB delete will still happen
                    }
                }
            }

            _db.Requests.Remove(record);
            await _db.SaveChangesAsync();

            return RedirectToAction("Index");
        }
       
    }
}
