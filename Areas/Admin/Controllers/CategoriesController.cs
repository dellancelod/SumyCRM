using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CategoriesController : Controller
    {
        private readonly DataManager dataManager;
        private readonly IWebHostEnvironment hostEnvironment;
        public CategoriesController(DataManager dataManager, IWebHostEnvironment hostEnvironment)
        {
            this.dataManager = dataManager;
            this.hostEnvironment = hostEnvironment;
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var categories = await dataManager.Categories.GetCategories()
                .OrderBy(x => x.Hidden)
                .ToListAsync();

            var pageItems = categories
                .Skip((page - 1) * pageSize)
                .Take(pageSize);


            var model = new PaginationViewModel<Category>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(categories.Count() / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
        }
    }
}
