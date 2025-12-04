using Microsoft.AspNetCore.Mvc;
using SumyCRM.Data;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Models;
using Microsoft.EntityFrameworkCore;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RequestsController : Controller
    {
        private readonly DataManager dataManager;
        public RequestsController(DataManager dataManager)
        {
            this.dataManager = dataManager;
        }
        public async Task<IActionResult> Index(int page = 1, bool completed = false)
        {
            int pageSize = 3; // Items per page

            var requests = await dataManager.Requests.GetRequests()
                .Include(r => r.Category)
                .Where(x => x.IsCompleted == completed)
                .OrderBy(x => x.RequestNumber)
                .ToListAsync();

            var pageItems = requests
                .Skip((page - 1) * pageSize)
                .Take(pageSize);


            var model = new PaginationViewModel<Request>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(requests.Count() / (double)pageSize)
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
            var order = await dataManager.Requests.GetRequestByIdAsync(id);

            if (order.IsCompleted == true)
            {
                await dataManager.Requests.DeleteRequestAsync(order);
            }

            return RedirectToAction(nameof(RequestsController.Index),
                nameof(RequestsController).Replace("Controller", string.Empty),
                new { page = 1, completed = true });
        }
        public IActionResult LoadRequests()
        {
            var ordersCount = dataManager.Requests.GetRequests().Count(o => o.IsCompleted == false);


            return Json(new
            {
                success = true,
                totalQuantity = ordersCount
            });
        }
    }
}
