using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CallsController : Controller
    {
        private readonly DataManager dataManager;
        public CallsController(DataManager dataManager)
        {
            this.dataManager = dataManager;
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var categories = await dataManager.CallRecordings.GetCallRecordings()
                .OrderByDescending(x => x.Hidden)
                .ToListAsync();

            var pageItems = categories
                .Skip((page - 1) * pageSize)
                .Take(pageSize);


            var model = new PaginationViewModel<CallRecording>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(categories.Count() / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (User.IsInRole("admin"))
            {
                var callRecording = await dataManager.CallRecordings.GetCallRecordingByIdAsync(id);

                await dataManager.CallRecordings.DeleteCallRecordingAsync(callRecording);
               
            }
            return RedirectToAction(nameof(RequestsController.Index),
                nameof(RequestsController).Replace("Controller", string.Empty),
                new { page = 1, completed = true });
        }
    }
}
