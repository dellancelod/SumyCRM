using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI.Audio;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly DataManager _dataManager;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(DataManager dataManager, UserManager<IdentityUser> userManager)
        {
            _dataManager = dataManager;
            _userManager = userManager;
        }

        // Show all requests
        public IActionResult Index()
        {
            var requests = _dataManager.Requests
               .GetRequests()
               .OrderByDescending(r => r.DateAdded)
               .Include(r => r.Category)// если есть DateAdded / CreatedAt
               .ToList();

            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedCategoryIds = _dataManager.UserCategories.GetUserCategories()
                        .Where(uc => uc.UserId == userId)
                    .Select(uc => uc.CategoryId)
                    .ToList();

                requests = requests.Where(r => allowedCategoryIds.Contains(r.CategoryId)).ToList();
            }

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
