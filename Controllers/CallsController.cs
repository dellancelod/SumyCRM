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
using System.Text.RegularExpressions;

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
                // "1" = OK, "0" = not found -> ask user to repeat
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
            {
                await audioText.CopyToAsync(stream);
            }
            using (var stream = new FileStream(fullPathName, FileMode.Create))
            {
                await audioName.CopyToAsync(stream);
            }
            using (var stream = new FileStream(fullPathAddress, FileMode.Create))
            {
                await audioAddress.CopyToAsync(stream);
            }

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

                // IMPORTANT: plain response for Asterisk
                return Content(found ? "1" : "0", "text/plain");
            }
            finally
            {
                // ===== cleanup temp files =====
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
                // intentionally ignored (логувати можна, але Asteriskу пофіг)
            }
        }
        private async Task<bool> HasWaterLeakMatch(string inputAddress)
        {
            var user = ParseAddr(inputAddress);
            if (string.IsNullOrWhiteSpace(user.Street)) return false;

            var leaks = await _dataManager.WaterLeakReports
                .GetWaterLeakReports()
                .Where(x => x.Status != "Done")
                .Select(x => x.Address)
                .ToListAsync();

            foreach (var a in leaks)
            {
                var leak = ParseAddr(a);

                // street must match (after your normalization)
                if (!StreetClose(user.Street, leak.Street))
                    continue;

                // range-aware / number-aware house match
                if (HouseMatches(leak.HouseRaw, user.HouseNumber, user.HouseRaw))
                    return true;

                // optional: fallback to your old heuristic if you want
                // if (IsAddressClose(NormalizeAddr(inputAddress), NormalizeAddr(a))) return true;
            }

            return false;
        }
        // very simple normalization (you can expand)
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
                 .Replace(",", " ")
                 .Replace("  ", " ");

            // keep digits/letters, collapse spaces
            var cleaned = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '/').ToArray());
            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Trim();
        }

        private sealed record AddrParts(string Street, string HouseRaw, int? HouseNumber);

        private static AddrParts ParseAddr(string s)
        {
            var n = NormalizeAddr(s);

            // Expect something like: "харківська 12-24" or "харківська 13" or "харківська 13/2"
            // We'll take last "house-ish" token as house, the rest as street.
            var parts = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return new AddrParts(n, "", null);

            var house = parts[^1];
            var street = string.Join(' ', parts.Take(parts.Length - 1)).Trim();

            // extract first integer from house (e.g., "13/2" -> 13, "13а" -> 13)
            var m = Regex.Match(house, @"^(?<num>\d+)");
            int? num = m.Success ? int.Parse(m.Groups["num"].Value) : null;

            return new AddrParts(street, house, num);
        }

        private static bool StreetClose(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            // strict equality after normalization is usually best; you can expand later (typos, etc.)
            return a == b;
        }

        private static bool HouseMatches(string leakHouseRaw, int? userHouseNum, string userHouseRaw)
        {
            // If user didn't give a number — can't do range logic reliably
            if (userHouseNum is null) return false;

            // Normalize dashes: "-", "–", "—"
            var h = leakHouseRaw.Replace('–', '-').Replace('—', '-');

            // 1) Range: "12-24"
            var rm = Regex.Match(h, @"^(?<a>\d+)\s*-\s*(?<b>\d+)");
            if (rm.Success)
            {
                var a = int.Parse(rm.Groups["a"].Value);
                var b = int.Parse(rm.Groups["b"].Value);
                var lo = Math.Min(a, b);
                var hi = Math.Max(a, b);
                return userHouseNum.Value >= lo && userHouseNum.Value <= hi;
            }

            // 2) Exact single number: "13" / "13а" / "13/2"
            var sm = Regex.Match(h, @"^(?<n>\d+)");
            if (sm.Success)
            {
                var n = int.Parse(sm.Groups["n"].Value);
                return userHouseNum.Value == n;
            }

            // 3) Fallback: raw compare (rare)
            return string.Equals(leakHouseRaw, userHouseRaw, StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsAddressClose(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

            // must share a house number (very important)
            var anum = ExtractHouse(a);
            var bnum = ExtractHouse(b);
            if (!string.IsNullOrEmpty(anum) && !string.IsNullOrEmpty(bnum) && anum != bnum)
                return false;

            // token overlap
            var at = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var bt = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var common = at.Intersect(bt).Count();

            // threshold: tune as needed
            return common >= 2;
        }

        private static string ExtractHouse(string s)
        {
            // naive: first token that contains a digit
            foreach (var t in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (t.Any(char.IsDigit)) return t;
            return "";
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
