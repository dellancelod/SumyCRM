using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

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

        // ----------------- helpers -----------------

        private string GetUserId() => _userManager.GetUserId(User) ?? string.Empty;

        private IQueryable<Request> BaseQuery()
        {
            return dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .AsQueryable();
        }

        private IQueryable<Request> ApplyFacilityAccessFilter(IQueryable<Request> query)
        {
            if (User.IsInRole("admin")) return query;

            var userId = GetUserId();

            // EXISTS (UserFacilities)
            return query.Where(r =>
                dataManager.UserFacilities.GetUserFacilities()
                    .Any(uf => uf.UserId == userId && uf.FacilityId == r.FacilityId)
            );
        }

        private static IQueryable<Request> ApplyFilters(
            IQueryable<Request> query,
            bool? isCompleted,
            Guid? categoryId,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            if (isCompleted.HasValue)
                query = query.Where(r => r.IsCompleted == isCompleted.Value);

            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
                query = query.Where(r => r.CategoryId == categoryId.Value);

            if (dateFrom.HasValue)
                query = query.Where(r => r.DateAdded >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(r => r.DateAdded < dateTo.Value.Date.AddDays(1)); // inclusive day

            return query;
        }

        private static IQueryable<Request> ApplySearch(IQueryable<Request> query, string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return query;

            term = term.Trim().ToLower();

            return query.Where(r =>
                r.RequestNumber.ToString().ToLower().Contains(term) ||
                (r.Name ?? "").ToLower().Contains(term) ||
                (r.Caller ?? "").ToLower().Contains(term) ||
                (r.Facility != null ? r.Facility.Name : "").ToLower().Contains(term) ||
                (r.Subcategory ?? "").ToLower().Contains(term) ||
                (r.Address ?? "").ToLower().Contains(term) ||
                (r.Text ?? "").ToLower().Contains(term) ||
                (r.Category != null ? r.Category.Title : "").ToLower().Contains(term)
            );
        }

        private async Task FillIndexViewBags(Guid? categoryId, DateTime? dateFrom, DateTime? dateTo, bool completed)
        {
            ViewBag.AreCompleted = completed;
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.DateFrom = dateFrom?.ToString("yyyy-MM-dd");
            ViewBag.DateTo = dateTo?.ToString("yyyy-MM-dd");

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();
        }

        private async Task FillEditViewBags(bool completed)
        {
            ViewBag.Completed = completed;

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();

            ViewBag.Facilities = await dataManager.Facilities.GetFacilities()
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        // ----------------- actions -----------------

        public async Task<IActionResult> Index(
            int page = 1,
            bool completed = false,
            Guid? categoryId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            const int pageSize = 8;

            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);
            query = ApplyFilters(query, completed, categoryId, dateFrom, dateTo);

            var total = await query.CountAsync();

            var pageItems = await query
                .OrderByDescending(r => r.RequestNumber)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            await FillIndexViewBags(categoryId, dateFrom, dateTo, completed);

            var model = new PaginationViewModel<Request>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid? id, bool completed = false)
        {
            await FillEditViewBags(completed);

            if (!id.HasValue || id.Value == Guid.Empty)
            {
                return View(new Request
                {
                    IsCompleted = false
                });
            }

            var entity = await BaseQuery()
                .FirstOrDefaultAsync(r => r.Id == id.Value);

            if (entity == null) return NotFound();

            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Request model, bool completed = false)
        {
            await FillEditViewBags(completed);

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
            var model = await BaseQuery()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (model == null) return NotFound();

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CompleteRequest(Guid id)
        {
            var request = await dataManager.Requests.GetRequestByIdAsync(id);
            if (request == null) return NotFound();

            request.IsCompleted = true;
            await dataManager.Requests.SaveRequestAsync(request);

            return RedirectToAction(nameof(Index), new { completed = false });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!User.IsInRole("admin"))
                return Forbid();

            var entity = await dataManager.Requests.GetRequestByIdAsync(id);
            if (entity == null) return NotFound();

            if (entity.IsCompleted)
                await dataManager.Requests.DeleteRequestAsync(entity);

            return RedirectToAction(nameof(Index), new { page = 1, completed = true });
        }

        // used by your "counter" widget
        public IActionResult LoadRequests()
        {
            var query = BaseQuery().Where(r => !r.IsCompleted);
            query = ApplyFacilityAccessFilter(query);

            return Json(new
            {
                success = true,
                totalQuantity = query.Count()
            });
        }

        [HttpGet]
        public async Task<IActionResult> Search(string term, bool completed = false)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);
            query = ApplyFilters(query, completed, null, null, null);
            query = ApplySearch(query, term);

            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .Take(300)
                .ToListAsync();

            var result = list.Select((r, idx) => new
            {
                index = idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility != null ? r.Facility.Name : "",
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                nameAudio = r.NameAudioFilePath,
                addressAudio = r.AddressAudioFilePath,
                audio = r.AudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(result);
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
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            query = ApplyFilters(query, isCompleted, categoryId, df, dt);
            query = ApplySearch(query, term);

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
