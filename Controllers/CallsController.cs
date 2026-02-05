using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI.Audio;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;
using System.Text.RegularExpressions;
using static SumyCRM.Services.CategoryConverter;
using static SumyCRM.Services.TranscriptService;

namespace SumyCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallsController : Controller
    {
        private readonly string _apiKey;
        private readonly DataManager _dataManager;
        private readonly IConfiguration _config;
        private readonly IGeocodingService _geo;

        public CallsController(DataManager dataManager, IConfiguration config, IGeocodingService geo)
        {
            _config = config;
            _dataManager = dataManager;
            _apiKey = config["OpenAI:ApiKey"];
            _geo = geo;
        }

        [HttpGet("log")]
        [AllowAnonymous]
        public async Task<IActionResult> Log(
            [FromQuery] string? callId,
            [FromQuery] string? caller,
            [FromQuery(Name = "event")] string? eventName,
            [FromQuery] string? data,
            [FromHeader(Name = "X-API-KEY")] string? apiKey,
            CancellationToken ct)
        {
            string secret = _config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (string.IsNullOrWhiteSpace(eventName))
                return BadRequest("Missing event");

            // Asterisk can send empty callId/caller sometimes, keep it tolerant.
            callId = (callId ?? "").Trim();
            caller = (caller ?? "").Trim();
            eventName = eventName.Trim();
            data = (data ?? "").Trim();

            // Optional safety: clamp lengths so nobody can flood DB with megabytes
            if (callId.Length > 64) callId = callId.Substring(0, 64);
            if (caller.Length > 32) caller = caller.Substring(0, 32);
            if (eventName.Length > 128) eventName = eventName.Substring(0, 128);
            if (data.Length > 1024) data = data.Substring(0, 1024);

            var ev = new CallEvent
            {
                CallId = callId,
                Caller = caller,
                Event = eventName,
                Data = data
            };

            await _dataManager.CallEvents.SaveCallEventAsync(ev);

            // Asterisk prefers simple responses
            return Content("OK", "text/plain");
        }

        [HttpPost("validate-address")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ValidateAddress(
            [FromForm] IFormFile audioAddress,
            [FromHeader(Name = "X-API-KEY")] string? apiKey,
            CancellationToken ct)
        {
            string secret = _config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (audioAddress == null || audioAddress.Length == 0)
                return BadRequest("No audio address");

            string? tmpPath = null;
            string? whisperPath = null;

            try
            {
                // save temp
                var tmpFolder = Path.Combine(Path.GetTempPath(), "sumycrm_calls");
                Directory.CreateDirectory(tmpFolder);

                tmpPath = Path.Combine(tmpFolder, $"{Guid.NewGuid()}_{Path.GetFileName(audioAddress.FileName)}");

                await using (var fs = new FileStream(tmpPath, FileMode.Create))
                    await audioAddress.CopyToAsync(fs, ct);

                // Whisper -> text address
                AudioClient audioClient = new("whisper-1", _apiKey);

                AudioTranscriptionOptions addressOptions = new()
                {
                    Language = "uk",
                    ResponseFormat = AudioTranscriptionFormat.Text,
                    Temperature = 0.0f,
                    Prompt =
                        "Розпізнай ТІЛЬКИ адресу в місті Суми (Україна). " +
                        "Формат: 'вул. ..., буд. ..., кв. ...' або 'просп. ..., буд. ...'. " +
                        "Збережи всі цифри, дроби (наприклад 12/1), корпуси, під'їзд. " +
                        "Без зайвих фраз."
                };

                whisperPath = await ConvertToWhisperWavAsync(tmpPath, ct);

                AudioTranscription tr = await audioClient.TranscribeAudioAsync(whisperPath, addressOptions);
                var addrText = CleanTranscript(tr.Text);

                // Validate via Nominatim (only Sumy results are accepted in your service)
                var geo = await _geo.GeocodeAsync(addrText, ct);

                // Asterisk-friendly plain response:
                return Content(geo != null ? "1" : "0", "text/plain");
            }
            finally
            {
                SafeDelete(tmpPath);
                SafeDelete(whisperPath);
            }
        }

        [HttpPost("upload")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken] // для curl / Asterisk
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile? audioName,
            [FromForm] IFormFile audioAddress,
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

            if (audioAddress == null || audioAddress.Length == 0)
                return BadRequest("No audio name");

            if (audioName == null || audioName.Length == 0)
                return BadRequest("No audio name");

            string folder = Path.Combine("wwwroot", "audio");
            Directory.CreateDirectory(folder);

            string fileNameForName = $"{Guid.NewGuid()}_{audioName.FileName}";
            string fileNameForAddress = $"{Guid.NewGuid()}_{audioAddress.FileName}";
            string fileNameForText = $"{Guid.NewGuid()}_{audioText.FileName}";

            string fullPathName = Path.Combine(folder, fileNameForName);
            string fullPathAddress = Path.Combine(folder, fileNameForAddress);
            string fullPathText = Path.Combine(folder, fileNameForText);

            using (var stream = new FileStream(fullPathText, FileMode.Create))
                await audioText.CopyToAsync(stream);

            using (var stream = new FileStream(fullPathName, FileMode.Create))
                await audioName.CopyToAsync(stream);

            using (var stream = new FileStream(fullPathAddress, FileMode.Create))
                await audioAddress.CopyToAsync(stream);

            // ===== Whisper STT =====
            AudioClient audioClient = new("whisper-1", _apiKey);

            AudioTranscriptionOptions nameOptions = new()
            {
                Language = "uk",
                ResponseFormat = AudioTranscriptionFormat.Text,
                Temperature = 0.0f,
                Prompt =
                    "Розпізнай ТІЛЬКИ ім'я та прізвище (за потреби по батькові). " +
                    "Українська. Без зайвих слів, без пояснень. " +
                    "Приклад: 'Іваненко Петро Олексійович'."
            };

            AudioTranscriptionOptions addressOptions = new()
            {
                Language = "uk",
                ResponseFormat = AudioTranscriptionFormat.Text,
                Temperature = 0.0f,
                Prompt =
                    "Розпізнай ТІЛЬКИ адресу в місті Суми (Україна). " +
                    "Формат: 'вул. ..., буд. ..., кв. ...' або 'просп. ..., буд. ...'. " +
                    "Збережи всі цифри, дроби (наприклад 12/1), корпуси, під'їзд. " +
                    "Без зайвих фраз."
            };

            AudioTranscriptionOptions textOptions = new()
            {
                Language = "uk",
                ResponseFormat = AudioTranscriptionFormat.Text,
                Temperature = 0.2f, // чуть мягче для длинной речи
                Prompt =
                    "Це звернення до міських служб. Українська мова. " +
                    "Передай зміст звернення одним текстом без вигаданих деталей. " +
                    "Тематика: транспорт/маршрутки, вода/водовідведення, тепло, ліфти, " +
                    "електроенергія, газ, благоустрій, освітлення, ремонт житла."
            };

            var whisperTextPath = await ConvertToWhisperWavAsync(fullPathText, HttpContext.RequestAborted);
            var whisperNamePath = await ConvertToWhisperWavAsync(fullPathName, HttpContext.RequestAborted);
            var whisperAddrPath = await ConvertToWhisperWavAsync(fullPathAddress, HttpContext.RequestAborted);

            AudioTranscription transcriptionText =
                await audioClient.TranscribeAudioAsync(whisperTextPath, textOptions);

            AudioTranscription transcriptionName =
                await audioClient.TranscribeAudioAsync(whisperNamePath, nameOptions);

            AudioTranscription transcriptionAddress =
                await audioClient.TranscribeAudioAsync(whisperAddrPath, addressOptions);

            string transcriptText = CleanTranscript(transcriptionText.Text);
            string transcriptName = CleanTranscript(transcriptionName.Text);
            string transcriptAddress = CleanTranscript(transcriptionAddress.Text);

            // ===== Save in DB =====
            if (!MenuToCategory.TryGetValue(menu_item, out var categoryId))
                return BadRequest("Unknown menu item");

            MenuToText.TryGetValue(menu_item, out var menu_text);

            var category = await _dataManager.Categories.GetCategoryByIdAsync(categoryId);
            if (category == null)
                return BadRequest("Category not found");

            var facility = await _dataManager.Facilities.GetFacilityByIdAsync(new Guid("9e10c51c-668e-4297-a18b-30cf66b2f2ae"));

            var record = new Request
            {
                RequestNumber = _dataManager.Requests.GetRequests().Count() + 1,
                CategoryId = category.Id,
                Category = category,
                Caller = caller,
                Name = transcriptName,
                Facility = facility,
                Subcategory = menu_text,
                Address = transcriptAddress,
                Text = transcriptText,
                IsCompleted = false,
                AudioFilePath = "/audio/" + fileNameForText,
                NameAudioFilePath = "/audio/" + fileNameForName,
                AddressAudioFilePath = "/audio/" + fileNameForAddress
            };

            await _dataManager.Requests.SaveRequestAsync(record);

            return Ok("Uploaded");
        }

        [HttpPost("check-waterleak")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CheckWaterLeak(
            [FromForm] IFormFile audioAddress,
            [FromHeader(Name = "X-API-KEY")] string? apiKey)
        {
            string secret = _config["UploadSecret"];
            if (apiKey != secret)
                return Unauthorized("Invalid API Key");

            if (audioAddress == null || audioAddress.Length == 0)
                return BadRequest("No audio address");

            string? tmpPath = null;
            string? whisperPath = null;

            try
            {
                // ===== save temp file =====
                var tmpFolder = Path.Combine(Path.GetTempPath(), "sumycrm_calls");
                Directory.CreateDirectory(tmpFolder);

                tmpPath = Path.Combine(
                    tmpFolder,
                    $"{Guid.NewGuid()}_{Path.GetFileName(audioAddress.FileName)}"
                );

                await using (var fs = new FileStream(tmpPath, FileMode.Create))
                    await audioAddress.CopyToAsync(fs);

                // ===== Whisper =====
                AudioClient audioClient = new("whisper-1", _apiKey);

                AudioTranscriptionOptions addressOptions = new()
                {
                    Language = "uk",
                    ResponseFormat = AudioTranscriptionFormat.Text,
                    Temperature = 0.0f,
                    Prompt =
                        "Розпізнай ТІЛЬКИ адресу в місті Суми (Україна). " +
                        "Формат: 'вул. ..., буд. ..., кв. ...' або 'просп. ..., буд. ...'. " +
                        "Без зайвих фраз."
                };

                whisperPath = await ConvertToWhisperWavAsync(tmpPath, HttpContext.RequestAborted);

                AudioTranscription tr =
                    await audioClient.TranscribeAudioAsync(whisperPath, addressOptions);

                var addr = CleanTranscript(tr.Text);

                // ===== check DB =====
                bool found = await HasWaterLeakMatch(addr);

                // Plain response for Asterisk / scripts
                // If you still need "1/0" instead: return Content(found ? "1" : "0", "text/plain");
                return Content(found ? "1" : "0", "text/plain");
            }
            finally
            {
                SafeDelete(tmpPath);
                SafeDelete(whisperPath);
            }
        }

        static void SafeDelete(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        private async Task<bool> HasWaterLeakMatch(string inputAddress)
        {
            var norm = NormalizeAddr(inputAddress);
            if (string.IsNullOrWhiteSpace(norm)) return false;

            // Load open leaks
            var leaks = await _dataManager.WaterLeakReports
                .GetWaterLeakReports()
                .Where(x => x.Status != "Done")
                .Select(x => x.Address)
                .ToListAsync();

            foreach (var a in leaks)
            {
                var n2 = NormalizeAddr(a);
                if (IsAddressClose(norm, n2)) return true;
            }

            return false;
        }

        // very simple normalization (expand as needed)
        private static string NormalizeAddr(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.ToLowerInvariant();

            s = s.Replace("м. суми", "")
                 .Replace("суми", "")
                 .Replace("вулиця", "вул")
                 .Replace("проспект", "просп")
                 .Replace("провулок", "пров")
                 .Replace(".", " ")
                 .Replace(",", " ");

            // normalize dashes (range separators)
            s = s.Replace('–', '-')  // en-dash
                 .Replace('—', '-')  // em-dash
                 .Replace('−', '-'); // minus sign

            while (s.Contains("  ")) s = s.Replace("  ", " ");

            // keep digits/letters, spaces, '/', '-' (for ranges)
            var cleaned = new string(s.Where(ch =>
                char.IsLetterOrDigit(ch) || ch == ' ' || ch == '/' || ch == '-'
            ).ToArray());

            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Trim();
        }

        private static bool IsAddressClose(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

            // Must match house number logic (supports ranges like 12-24)
            var anumToken = ExtractHouseToken(a);
            var bnumToken = ExtractHouseToken(b);

            if (!string.IsNullOrEmpty(anumToken) && !string.IsNullOrEmpty(bnumToken))
            {
                if (!HouseTokensMatch(anumToken, bnumToken))
                    return false;
            }

            // Token overlap (street name etc.)
            var at = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var bt = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var common = at.Intersect(bt).Count();

            return common >= 2;
        }

        private static string ExtractHouseToken(string s)
        {
            // First token that contains a digit (can be "13", "13а", "12/1", "12-24")
            foreach (var t in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (t.Any(char.IsDigit)) return t.Trim();
            return "";
        }

        private static bool HouseTokensMatch(string inputToken, string leakToken)
        {
            // If leakToken is a range (12-24), and inputToken is 13 -> match
            // If both are single numbers -> exact numeric match (leading digits)
            // Otherwise fallback to string equality for things like 19/2, 10к1 etc.

            inputToken = (inputToken ?? "").Trim();
            leakToken = (leakToken ?? "").Trim();

            // normalize dash
            inputToken = NormalizeDash(inputToken);
            leakToken = NormalizeDash(leakToken);

            // Parse input leading number
            if (!TryParseLeadingInt(inputToken, out int inputNum))
            {
                // can't parse -> safest: exact compare
                return string.Equals(inputToken, leakToken, StringComparison.OrdinalIgnoreCase);
            }

            // If leak is a range
            if (TryParseRange(leakToken, out int from, out int to))
            {
                if (from > to) (from, to) = (to, from);
                return inputNum >= from && inputNum <= to;
            }

            // Leak is not a range: compare leading ints if possible
            if (TryParseLeadingInt(leakToken, out int leakNum))
                return inputNum == leakNum;

            // Fallback: exact compare
            return string.Equals(inputToken, leakToken, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDash(string s)
            => (s ?? "")
                .Replace('–', '-')
                .Replace('—', '-')
                .Replace('−', '-');

        private static bool TryParseLeadingInt(string token, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(token)) return false;

            // take leading digits only (e.g. "13а" -> 13)
            var m = Regex.Match(token, @"^\s*(\d+)");
            if (!m.Success) return false;

            return int.TryParse(m.Groups[1].Value, out value);
        }

        private static bool TryParseRange(string token, out int from, out int to)
        {
            from = 0; to = 0;
            if (string.IsNullOrWhiteSpace(token)) return false;

            // Accept: 12-24, 12–24, 12—24 (already normalized to '-')
            var m = Regex.Match(token, @"^\s*(\d+)\s*-\s*(\d+)\s*$");
            if (!m.Success) return false;

            return int.TryParse(m.Groups[1].Value, out from) && int.TryParse(m.Groups[2].Value, out to);
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

            string ext = Path.GetExtension(audio.FileName);
            if (string.IsNullOrEmpty(ext))
                ext = ".wav";

            string safeCaller = caller.Replace("+", "").Replace(" ", "");
            string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeCaller}_{Guid.NewGuid()}{ext}";
            string fullPath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                await audio.CopyToAsync(stream);

            var record = new CallRecording
            {
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
