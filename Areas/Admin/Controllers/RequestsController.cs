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
                .Include(r => r.Facility)
                .Where(r => r.IsCompleted == completed)
                .OrderByDescending(r => r.RequestNumber)
                .AsQueryable();

            // НЕ админ — фильтруем по доступным категориям
            if (!User.IsInRole("admin"))
            {
                var userId = _userManager.GetUserId(User);

                var allowedFacilitiesIds = await dataManager.UserFacilities.GetUserFacilities()
                    .Where(uc => uc.UserId == userId)
                    .Select(uc => uc.FacilityId)
                    .ToListAsync();

                query = query.Where(r => allowedFacilitiesIds.Contains(r.FacilityId));
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

        [HttpGet]
        public async Task<IActionResult> Edit(Guid? id, bool completed = false)
        {
            ViewBag.Completed = completed;

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();

            ViewBag.Facilities = await dataManager.Facilities.GetFacilities()
                .OrderBy(f => f.Name)
                .ToListAsync();

            if (!id.HasValue || id.Value == Guid.Empty)
            {
                return View(new Request
                {
                    Id = Guid.Empty,
                    DateAdded = DateTime.UtcNow,
                    IsCompleted = false
                });
            }

            var entity = await dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .FirstOrDefaultAsync(r => r.Id == id.Value);

            if (entity == null) return NotFound();

            return View(entity);
        }

        // ========= EDIT (POST) =========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Request model, bool completed = false)
        {
            ViewBag.Completed = completed;

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();

            ViewBag.Facilities = await dataManager.Facilities.GetFacilities()
                .OrderBy(f => f.Name)
                .ToListAsync();

            if (!ModelState.IsValid)
                return View(model);

            if (model.Id == Guid.Empty)
            {
                model.IsCompleted = false;

                await dataManager.Requests.SaveRequestAsync(model);
            }
            else
            {
                var entity = await dataManager.Requests.GetRequestByIdAsync(model.Id);
                if (entity == null) return NotFound();

                entity.RequestNumber = model.RequestNumber;
                entity.Name = model.Name;
                entity.Caller = model.Caller;
                entity.Subcategory = model.Subcategory;
                entity.Address = model.Address;
                entity.Text = model.Text;
                entity.CategoryId = model.CategoryId;
                entity.FacilityId = model.FacilityId;

                await dataManager.Requests.SaveRequestAsync(entity);
            }

            return RedirectToAction(nameof(Index), new { page = 1, completed });
        }

        public async Task<IActionResult> Show(Guid id)
        {
            var model = await dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (model == null) return NotFound();

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

                var allowedFacilityIds = await dataManager.UserFacilities.GetUserFacilities()
                    .Where(uc => uc.UserId == userId)
                    .Select(uc => uc.FacilityId)
                    .ToListAsync();

                query = query.Where(r => allowedFacilityIds.Contains(r.FacilityId));
            }

            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim().ToLower();

                query = query.Where(r =>
                    (r.RequestNumber.ToString() ?? "").ToLower().Contains(term) ||
                    (r.Name ?? "").ToLower().Contains(term) ||
                    (r.Caller ?? "").ToLower().Contains(term) ||
                    (r.Facility.Name ?? "").ToLower().Contains(term) ||
                    (r.Subcategory ?? "").ToLower().Contains(term) ||
                    (r.Address ?? "").ToLower().Contains(term) ||
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
                facility = r.Facility,
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
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

                var allowedFacilityIds = dataManager.UserFacilities.GetUserFacilities()
                        .Where(uc => uc.UserId == userId)
                        .Select(uc => uc.FacilityId)
                        .ToList();

                query = query.Where(r => allowedFacilityIds.Contains(r.FacilityId));
            }

            return Json(new
            {
                success = true,
                totalQuantity = query.Count()
            });
        }
        [HttpGet]
        public async Task<IActionResult> GetList(
            string? term,
            bool? isCompleted,
            Guid? categoryId,
            string? dateFrom,
            string? dateTo,
            int page = 1,
            int pageSize = 8)
        {
            var query = dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .AsQueryable();

            var userId = _userManager.GetUserId(User);

            if (!User.IsInRole("admin"))
            {
                query = query.Where(r =>
                    dataManager.UserFacilities.GetUserFacilities()
                        .Any(uf => uf.UserId == userId && uf.FacilityId == r.FacilityId)
                );
            }

            // completed / active
            if (isCompleted.HasValue)
                query = query.Where(r => r.IsCompleted == isCompleted.Value);

            // фильтр по категории
            if (categoryId.HasValue)
            {
                query = query.Where(r => r.CategoryId == categoryId.Value);
            }

            // фильтр по датам
            if (!string.IsNullOrWhiteSpace(dateFrom) &&
                DateTime.TryParse(dateFrom, out var df))
            {
                query = query.Where(r => r.DateAdded >= df);
            }

            if (!string.IsNullOrWhiteSpace(dateTo) &&
                DateTime.TryParse(dateTo, out var dt))
            {
                // до конца дня
                dt = dt.Date.AddDays(1).AddTicks(-1);
                query = query.Where(r => r.DateAdded <= dt);
            }

            // поиск по строке
            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim().ToLower();
                query = query.Where(r =>
                    (r.Name ?? "").ToLower().Contains(term) ||
                    (r.Caller ?? "").ToLower().Contains(term) ||
                    (r.Facility == null ? "" : r.Facility.Name).ToLower().Contains(term) ||
                    (r.Subcategory ?? "").ToLower().Contains(term) ||
                    (r.Text ?? "").ToLower().Contains(term) ||
                    (r.Address ?? "").ToLower().Contains(term) ||
                    (r.Category.Title ?? "").ToLower().Contains(term)
                );
            }

            var total = await query.CountAsync();

            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = list.Select((r, idx) => new
            {
                index = (page - 1) * pageSize + idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility != null ? r.Facility.Name : "",
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                audio = r.AudioFilePath,
                nameAudio = r.NameAudioFilePath,
                addressAudio = r.AddressAudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(new
            {
                items,
                total,
                currentPage = page,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

    }
}
