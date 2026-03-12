using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SumyCRM.Data;
using SumyCRM.Models;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Audio;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using TraineeApplication.Model;

namespace SumyCRM.Controllers
{
    public class HomeController : Controller
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly SignInManager<ApplicationUser> signInManager;

        private readonly DataManager _dataManager;
        public HomeController(UserManager<ApplicationUser> userMgr, SignInManager<ApplicationUser> signInMgr,
            DataManager dataManager, AppDbContext db)
        {
            userManager = userMgr;
            signInManager = signInMgr;
            _dataManager = dataManager;
        }
        [AllowAnonymous]
        public IActionResult Index()
        {
            ViewBag.Title = "Вхід";
            return View(new LoginViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                ApplicationUser user = await userManager.FindByNameAsync(model.Username);
                if (user != null)
                {
                    await signInManager.SignOutAsync();
                    Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
                    if (result.Succeeded)
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                }
                ModelState.AddModelError(nameof(LoginViewModel.Username), "Невірний логін або пароль");
            }
            return View("Index", model);
        }


        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
       
    }

}
