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
        private readonly UserManager<IdentityUser> userManager;
        private readonly SignInManager<IdentityUser> signInManager;

        private readonly string _apiKey;
        private readonly DataManager _dataManager;
        public HomeController(UserManager<IdentityUser> userMgr, SignInManager<IdentityUser> signInMgr,
            DataManager dataManager, AppDbContext db, IConfiguration config)
        {
            userManager = userMgr;
            signInManager = signInMgr;
            _apiKey = config["OpenAI:ApiKey"];
            _dataManager = dataManager;
        }
        [AllowAnonymous]
        public IActionResult Index()
        {
            ViewBag.Title = "Логін";
            return View(new LoginViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                IdentityUser user = await userManager.FindByNameAsync(model.Username);
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
