using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;
using static SumyCRM.Services.TranscriptService;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AbonentsController : Controller
    {
        // GET: AbonentsController
        private readonly DataManager dataManager;

        public AbonentsController(DataManager dataManager)
        {
            this.dataManager = dataManager;
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var query = dataManager.Abonents.GetAbonents()
                    .AsQueryable();

            var total = await query.CountAsync();

            var pageItems = await query
                .OrderBy(f => f.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();


            var model = new PaginationViewModel<Abonent>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Search(string term)
        {
            var query = dataManager.Abonents.GetAbonents();

            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim().ToLower();

                query = query.Where(f =>
                    (f.Name ?? "").ToLower().Contains(term) ||
                    (f.Phone ?? "").ToLower().Contains(term) ||
                    (f.FullAddress ?? "").ToLower().Contains(term) ||
                    (f.Comment ?? "").ToLower().Contains(term)
                );
            }

            var list = await query
                .OrderBy(f => f.Name)
                .Take(200)
                .ToListAsync();

            var result = list.Select((f, idx) => new
            {
                index = idx + 1,
                id = f.Id,
                name = f.Name,
                phone = f.Phone,
                fullAddress = f.FullAddress,
                comment = f.Comment,
                hidden = f.Hidden
            });

            return Json(result);
        }
        public async Task<IActionResult> Edit(Guid id)
        {
            var entity = id == default ? new Abonent() : await dataManager.Abonents.GetAbonentByIdAsync(id);

            return View(entity);
        }
        [HttpPost]
        [RequestSizeLimit(512 * 1024 * 1024)]
        public async Task<IActionResult> Edit(Abonent model, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(model.Phone))
                model.Phone = NormalizePhone(model.Phone);

            // Проверка дубликата телефона
            var existing = await dataManager.Abonents
                .GetAbonents()
                .FirstOrDefaultAsync(a => a.Phone == model.Phone && a.Id != model.Id);

            if (existing != null)
            {
                ModelState.AddModelError("Phone", "Абонент з таким телефоном вже існує");
            }

            if (ModelState.IsValid)
            {
                await dataManager.Abonents.SaveAbonentAsync(model);

                return RedirectToAction(
                    nameof(AbonentsController.Index),
                    nameof(AbonentsController).Replace("Controller", string.Empty)
                );
            }

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (User.IsInRole("admin"))
            {
                await dataManager.Abonents.DeleteAbonentAsync(id);
            }
            // тут, думаю, у тебя вообще должна быть редирект на ScheduleController, а не CategoriesController
            return RedirectToAction(
                nameof(AbonentsController.Index),
                nameof(AbonentsController).Replace("Controller", string.Empty)
            );
        }

    }
}
