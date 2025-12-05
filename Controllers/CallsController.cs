using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Audio;
using SumyCRM.Data;
using SumyCRM.Models;
using static SumyCRM.Services.CategoryConverter;

namespace SumyCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallsController : Controller
    {
        private readonly string _apiKey;
        private readonly DataManager _dataManager;
        private readonly IConfiguration _config;

        public CallsController(DataManager dataManager, IConfiguration config)
        {
            _config = config;
            _dataManager = dataManager;
            _apiKey = config["OpenAI:ApiKey"];
        }

        [HttpPost("upload")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken] // для curl / Asterisk
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile? audioName,
            [FromForm] IFormFile? audioText,
            [FromForm(Name = "caller")] string? caller,
            [FromForm(Name = "menu_item")] string? menu_item,
            [FromHeader(Name = "X-API-KEY")] string? apiKey)
        {
            string secret = _config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (audioText == null || audioText.Length == 0)
                return BadRequest("No audio text");

            if (audioName == null || audioName.Length == 0)
                return BadRequest("No audio name");

            string folder = Path.Combine("wwwroot", "audio");

            Directory.CreateDirectory(folder);

            string fileNameForName = $"{Guid.NewGuid()}_{audioName.FileName}";
            string fileNameForText = $"{Guid.NewGuid()}_{audioText.FileName}";

            string fullPathName = Path.Combine(folder, fileNameForName);
            string fullPathText = Path.Combine(folder, fileNameForText);

            using (var stream = new FileStream(fullPathText, FileMode.Create))
            {
                await audioText.CopyToAsync(stream);
            }
            using (var stream = new FileStream(fullPathName, FileMode.Create))
            {
                await audioName.CopyToAsync(stream);
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
            AudioTranscription transcriptionText =
                     await audioClient.TranscribeAudioAsync(fullPathText, options);

            AudioTranscription transcriptionName =
                     await audioClient.TranscribeAudioAsync(fullPathName, options);

            string transcriptText = CleanTranscript(transcriptionText.Text);
            string transcriptName = CleanTranscript(transcriptionName.Text);

            // ===== Save in DB =====
            if (!MenuToCategory.TryGetValue(menu_item, out var categoryId))
                return BadRequest("Unknown menu item");

            MenuToText.TryGetValue(menu_item, out var menu_text);

            var category = await _dataManager.Categories.GetCategoryByIdAsync(categoryId);
            if (category == null)
                return BadRequest("Category not found");

            var record = new Request
            {
                RequestNumber = _dataManager.Requests.GetRequests().Count() + 1,
                CategoryId = category.Id,
                Category = category,
                Caller = caller,
                Name = transcriptName,
                Subcategory = menu_text,
                Text = transcriptText,
                IsCompleted = false,
                AudioFilePath = "/audio/" + fileNameForText,
                NameAudioFilePath = "/audio/" + fileNameForName
            };

            await _dataManager.Requests.SaveRequestAsync(record);

            return Ok("Uploaded");
        }

        private static string CleanTranscript(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var trimmed = text.Trim();

            // типові «хвости» з відео/шуму
            var noisePhrases = new[]
            {
                "дякую за перегляд",
                "дякуємо за перегляд",
                "спасибо за просмотр",
                "thank you for watching"
            };

            if (noisePhrases.Any(p =>
                    trimmed.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(p + " ", StringComparison.OrdinalIgnoreCase)))
            {
                return string.Empty;
            }

            // додатковий жорсткий фільтр на дуже короткий текст
            if (trimmed.Length <= 3)
                return string.Empty;

            return trimmed;
        }
    }
}
