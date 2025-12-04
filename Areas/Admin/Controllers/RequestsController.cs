using Microsoft.AspNetCore.Mvc;
using SumyCRM.Data;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RequestsController : Controller
    {
        private readonly DataManager dataManager;
        private readonly UserManager<IdentityUser> _userManager;
        public RequestsController(DataManager dataManager, UserManager<IdentityUser> userManager)
        {
            this.dataManager = dataManager;
            _userManager = userManager;
        }
        public async Task<IActionResult> Index(int page = 1, bool completed = false)
        {
            int pageSize = 3;

            // ← БАЗОВЫЙ запрос как IQueryable
            var query = dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Where(r => r.IsCompleted == completed)
                .OrderBy(r => r.RequestNumber)
                .AsQueryable();

            // ← НЕ АДМИН — фильтруем по категориям
            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedCategoryIds = await dataManager.UserCategories.GetUserCategories()
                        .Where(uc => uc.UserId == userId)
                        .Select(uc => uc.CategoryId)
                        .ToListAsync();

                query = query.Where(r => allowedCategoryIds.Contains(r.CategoryId));
            }

            // ← Общее количество после всех фильтров
            var total = await query.CountAsync();

            // ← Текущая страница
            var pageItems = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Модель
            var model = new PaginationViewModel<Request>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                PageSize = pageSize
            };

            ViewBag.AreCompleted = completed;

            return View(model);
        }
        public async Task<IActionResult> Show(Guid id)
        {
            var model = await dataManager.Requests.GetRequestByIdAsync(id);

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> CompleteRequest(Guid id)
        {
            var request = await dataManager.Requests.GetRequestByIdAsync(id);

            if (request == null)
            {
                return NotFound();
            }

            request.IsCompleted = true;

            await dataManager.Requests.SaveRequestAsync(request);


            return RedirectToAction("Index");
        }
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (User.IsInRole("admin"))
            {
                var order = await dataManager.Requests.GetRequestByIdAsync(id);

                if (order.IsCompleted == true)
                {
                    await dataManager.Requests.DeleteRequestAsync(order);
                }
            }
            return RedirectToAction(nameof(RequestsController.Index),
                nameof(RequestsController).Replace("Controller", string.Empty),
                new { page = 1, completed = true });
        }
        public IActionResult LoadRequests()
        {
            var query = dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Where(r => r.IsCompleted == false)
                .OrderBy(r => r.RequestNumber)
                .AsQueryable();

            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedCategoryIds = dataManager.UserCategories.GetUserCategories()
                        .Where(uc => uc.UserId == userId)
                        .Select(uc => uc.CategoryId)
                        .ToList();

                query = query.Where(r => allowedCategoryIds.Contains(r.CategoryId));
            }

            return Json(new
            {
                success = true,
                totalQuantity = query.Count()
            });
        }
    }
}
