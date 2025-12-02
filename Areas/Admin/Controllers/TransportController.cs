using Microsoft.AspNetCore.Mvc;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TransportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
