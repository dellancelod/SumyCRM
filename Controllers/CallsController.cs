using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Audio;
using SumyCRM.Data;
using SumyCRM.Models;
using static SumyCRM.Services.TranscriptService;
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
        [HttpPost("record")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken] // для curl / Asterisk
        public async Task<IActionResult> Record(
           [FromForm] IFormFile? audio,
           [FromForm(Name = "caller")] string? caller,
           [FromHeader(Name = "X-API-KEY")] string? apiKey)
        {
            string secret = _config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (audio == null || audio.Length == 0)
                return BadRequest("No audio");

            if (string.IsNullOrWhiteSpace(caller))
                caller = "unknown";

            // Куда сохранять записи
            string folder = Path.Combine("wwwroot", "callrecords");
            Directory.CreateDirectory(folder);

            // Имя файла: 20251205_134500_+380677673165_guid.wav
            string ext = Path.GetExtension(audio.FileName);
            if (string.IsNullOrEmpty(ext))
                ext = ".wav"; // по умолчанию

            string safeCaller = caller.Replace("+", "").Replace(" ", "");
            string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeCaller}_{Guid.NewGuid()}{ext}";
            string fullPath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // Сохраняем запись в БД
            var record = new CallRecording
            {
                // Если хочешь автонумерацию — можешь заменить на собственную логику
                CallNumber = _dataManager.CallRecordings.GetCallRecordings().Count() + 1,
                Caller = caller,
                AudioFilePath = "/callrecords/" + fileName
            };

            await _dataManager.CallRecordings.SaveCallRecordingAsync(record);

            return Ok(new
            {
                message = "Call record saved",
                id = record.Id,
                number = record.CallNumber,
                caller = record.Caller,
                url = record.AudioFilePath
            });
        }

    }
}
