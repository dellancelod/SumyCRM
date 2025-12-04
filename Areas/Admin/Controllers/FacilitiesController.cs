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
        public FacilitiesController(DataManager dataManager)
        {
            this.dataManager = dataManager;
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var facilities = await dataManager.Facilities.GetFacilities()
                .OrderBy(x => x.Hidden)
                .ToListAsync();

            var pageItems = facilities
                .Skip((page - 1) * pageSize)
                .Take(pageSize);


            var model = new PaginationViewModel<Facility>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(facilities.Count() / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
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
