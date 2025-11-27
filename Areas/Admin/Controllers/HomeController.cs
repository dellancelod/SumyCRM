using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI.Audio;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly DataManager _dataManager;

        public HomeController(DataManager dataManager)
        {
            _dataManager = dataManager;
        }

        // Show all requests
        public IActionResult Index()
        {
            var requests = _dataManager.Requests
               .GetRequests()
               .OrderByDescending(r => r.DateAdded)   // если есть DateAdded / CreatedAt
               .ToList();

            var activeCount = requests.Count(r => !r.IsCompleted);
            var completedCount = requests.Count(r => r.IsCompleted);

            var vm = new DashboardViewModel
            {
                Requests = requests,
                ActiveCount = activeCount,
                CompletedCount = completedCount,
                CategoryStats = requests
                    .GroupBy(r => r.Category?.Title ?? "Без категорії") // или r.Category.Name, или r.Text — как у тебя
                    .Select(g => new CategoryStat
                    {
                        Name = g.Key ?? "Без категорії",
                        Count = g.Count()
                    })
                    .ToList()
            };

            return View(vm);
        }
    }
}
