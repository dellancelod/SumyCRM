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
        public async Task<IActionResult> Index(
            int page = 1,
            bool completed = false,
            Guid? categoryId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            int pageSize = 8;

            // базовый запрос
            var query = dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Where(r => r.IsCompleted == completed)
                .OrderBy(r => r.RequestNumber)
                .AsQueryable();

            // НЕ админ — фильтруем по доступным категориям
            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedCategoryIds = await dataManager.UserCategories.GetUserCategories()
                    .Where(uc => uc.UserId == userId)
                    .Select(uc => uc.CategoryId)
                    .ToListAsync();

                query = query.Where(r => allowedCategoryIds.Contains(r.CategoryId));
            }

            // ====== ФИЛЬТР ПО КАТЕГОРИИ ======
            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
            {
                query = query.Where(r => r.CategoryId == categoryId.Value);
            }

            // ====== ФИЛЬТР ПО ДАТЕ ======
            if (dateFrom.HasValue)
            {
                // от начала дня
                var from = dateFrom.Value.Date;
                query = query.Where(r => r.DateAdded >= from);
            }

            if (dateTo.HasValue)
            {
                // до конца дня (включительно)
                var to = dateTo.Value.Date.AddDays(1);
                query = query.Where(r => r.DateAdded < to);
            }

            // общее количество после фильтров
            var total = await query.CountAsync();

            // текущая страница
            var pageItems = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = new PaginationViewModel<Request>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                PageSize = pageSize
            };

            ViewBag.AreCompleted = completed;

            // подкинем фильтры обратно в ViewBag, чтобы сохранить значения в инпутах
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.DateFrom = dateFrom?.ToString("yyyy-MM-dd");
            ViewBag.DateTo = dateTo?.ToString("yyyy-MM-dd");

            // список категорий для select-а
            var categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();
            ViewBag.Categories = categories;

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
        [HttpGet]
        public async Task<IActionResult> Search(string term, bool completed = false)
        {
            // базовий запит
            var query = dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Where(r => r.IsCompleted == completed)
                .OrderBy(r => r.RequestNumber)
                .AsQueryable();

            // фільтр за роллю (як у Index)
            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedCategoryIds = await dataManager.UserCategories.GetUserCategories()
                    .Where(uc => uc.UserId == userId)
                    .Select(uc => uc.CategoryId)
                    .ToListAsync();

                query = query.Where(r => allowedCategoryIds.Contains(r.CategoryId));
            }

            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim().ToLower();

                query = query.Where(r =>
                    (r.RequestNumber.ToString() ?? "").ToLower().Contains(term) ||
                    (r.Name ?? "").ToLower().Contains(term) ||
                    (r.Caller ?? "").ToLower().Contains(term) ||
                    (r.Subcategory ?? "").ToLower().Contains(term) ||
                    (r.Text ?? "").ToLower().Contains(term) ||
                    ((r.Category != null ? r.Category.Title : "") ?? "").ToLower().Contains(term)
                );
            }

            var list = await query
                .Take(300) // обмеження, щоб не завалити браузер
                .ToListAsync();

            var result = list.Select((r, idx) => new
            {
                index = idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                text = r.Text,
                nameAudio = r.NameAudioFilePath,
                audio = r.AudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(result);
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
