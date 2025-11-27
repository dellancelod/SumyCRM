using Microsoft.AspNetCore.Mvc;
using TraineeApplication.Model;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TransportController : Controller
    {
        public IActionResult Index()
        {
            return View(new LoginViewModel());
        }

    }
}
