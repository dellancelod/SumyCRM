using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class FacilitiesController : Controller
    {
        private readonly DataManager dataManager;

        private static readonly Guid NONE_FACILITY_ID = new Guid("9e10c51c-668e-4297-a18b-30cf66b2f2ae");

        public FacilitiesController(DataManager dataManager)
        {
            this.dataManager = dataManager;
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var query = dataManager.Facilities.GetFacilities()
                    .Where(f => f.Id != NONE_FACILITY_ID)
                    .AsQueryable();

            var total = await query.CountAsync();

            var pageItems = await query
                .OrderBy(f => f.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();


            var model = new PaginationViewModel<Facility>
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
            var query = dataManager.Facilities.GetFacilities()
                    .Where(f => f.Id != NONE_FACILITY_ID);

            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim().ToLower();

                query = query.Where(f =>
                    (f.Name ?? "").ToLower().Contains(term) ||
                    (f.Description ?? "").ToLower().Contains(term) ||
                    (f.Address ?? "").ToLower().Contains(term) ||
                    (f.Phones ?? "").ToLower().Contains(term)
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
                description = f.Description,
                address = f.Address,
                phones = f.Phones,
                hidden = f.Hidden
            });

            return Json(result);
        }
        public async Task<IActionResult> Edit(Guid id)
        {
            var entity = id == default ? new Facility() : await dataManager.Facilities.GetFacilityByIdAsync(id);

            return View(entity);
        }
        [HttpPost]
        [RequestSizeLimit(512 * 1024 * 1024)]
        public async Task<IActionResult> Edit(Facility model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {

                await dataManager.Facilities.SaveFacilityAsync(model);

                return RedirectToAction(nameof(FacilitiesController.Index), nameof(FacilitiesController).Replace("Controller", string.Empty));
            }

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (User.IsInRole("admin"))
            {
                await dataManager.Facilities.DeleteFacilityAsync(id);
            }
            // тут, думаю, у тебя вообще должна быть редирект на ScheduleController, а не CategoriesController
            return RedirectToAction(
                nameof(FacilitiesController.Index),
                nameof(FacilitiesController).Replace("Controller", string.Empty)
            );
        }
    }
}
