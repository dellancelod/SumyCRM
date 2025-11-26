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
        private readonly AppDbContext _db;
        public HomeController(UserManager<IdentityUser> userMgr, SignInManager<IdentityUser> signInMgr, AppDbContext db, IConfiguration config)
        {
            userManager = userMgr;
            signInManager = signInMgr;
            _apiKey = config["OpenAI:ApiKey"];
            _db = db;
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
            return View(model);
        }


        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Upload(IFormFile audio,
           string caller, string menu_item,
           [FromHeader(Name = "X-API-KEY")] string apiKey,
           [FromServices] IConfiguration config)
        {
            string secret = config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (audio == null || audio.Length == 0)
                return BadRequest("No audio file");

            string folder = Path.Combine("wwwroot", "audio");

            Directory.CreateDirectory(folder);

            string fileName = $"{Guid.NewGuid()}_{audio.FileName}";
            string fullPath = Path.Combine(folder, fileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // ===== Whisper STT =====

            AudioClient audioClient = new("whisper-1", _apiKey);
            AudioTranscriptionOptions options = new()
            {
                // Force Ukrainian
                Language = "uk",

                // можно не задавать, по умолчанию вернётся просто текст
                ResponseFormat = AudioTranscriptionFormat.Text
            };
            AudioTranscription transcription =
                     await audioClient.TranscribeAudioAsync(fullPath, options);
            string transcript = transcription.Text ?? "(empty)";

            // ===== Save in DB =====
            var record = new Request
            {
                Caller = caller,
                Text = menu_item,
                Address = transcript,
                AudioFilePath = "/audio/" + fileName,
                CreatedAt = DateTime.UtcNow
            };
            _db.Requests.Add(record);
            await _db.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }

}
