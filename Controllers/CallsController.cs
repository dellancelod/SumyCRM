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
                AudioFilePath = "/audio/" + Path.GetFileName(fullPathText),
                NameAudioFilePath = "/audio/" + Path.GetFileName(fullPathName),
                AddressAudioFilePath = "/audio/" + Path.GetFileName(fullPathAddress)
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

                // You asked for "found" when in range like Харківська 12-24 and user says Харківська 13
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
                // intentionally ignored
            }
        }

        private async Task<bool> HasWaterLeakMatch(string inputAddress)
        {
            var norm = NormalizeAddr(inputAddress);
            if (string.IsNullOrWhiteSpace(norm)) return false;

            // load open leaks
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

        // Normalization: keep letters/digits/space and allow "/" and "-" for house (19/2, 12-24)
        private static string NormalizeAddr(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";

            s = s.ToLowerInvariant();

            // unify dashes for ranges (– — etc.)
            s = s.Replace('–', '-')
                 .Replace('—', '-')
                 .Replace('−', '-');

            // remove city words
            s = s.Replace("м. суми", " ")
                 .Replace("місто суми", " ")
                 .Replace("суми", " ");

            // common street-type normalization
            s = s.Replace("вулиця", "вул")
                 .Replace("проспект", "просп")
                 .Replace("провулок", "пров")
                 .Replace("площа", "пл")
                 .Replace("бульвар", "бул");

            // punctuation to spaces (but keep '-' and '/')
            s = s.Replace(".", " ")
                 .Replace(",", " ")
                 .Replace("№", " ")
                 .Replace(":", " ")
                 .Replace(";", " ")
                 .Replace("\t", " ")
                 .Replace("\n", " ")
                 .Replace("\r", " ");

            // keep digits/letters, spaces, '/', '-' (range)
            var cleaned = new string(s.Where(ch =>
                char.IsLetterOrDigit(ch) || ch == ' ' || ch == '/' || ch == '-').ToArray());

            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Trim();
        }

        private readonly record struct HouseInfo(
            bool HasAny,
            int? SingleNumber,
            int? RangeFrom,
            int? RangeTo,
            string? RawToken
        );

        // Tries to parse something like: 13, 19/2, 12а, 12-24
        private static HouseInfo ParseHouse(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new HouseInfo(false, null, null, null, null);

            // pick first token that has a digit (e.g. "13", "12-24", "19/2", "12а")
            var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var t0 in tokens)
            {
                var t = t0.Trim();
                if (!t.Any(char.IsDigit)) continue;

                // RANGE: 12-24 (also already normalized –/— to '-')
                var mRange = Regex.Match(t, @"^(?<a>\d{1,4})\s*-\s*(?<b>\d{1,4})");
                if (mRange.Success)
                {
                    int a = int.Parse(mRange.Groups["a"].Value);
                    int b = int.Parse(mRange.Groups["b"].Value);
                    int from = Math.Min(a, b);
                    int to = Math.Max(a, b);
                    return new HouseInfo(true, null, from, to, t0);
                }

                // SINGLE: take first number (13, 19/2, 12а -> single=12)
                var mSingle = Regex.Match(t, @"^(?<n>\d{1,4})");
                if (mSingle.Success)
                {
                    int n = int.Parse(mSingle.Groups["n"].Value);
                    return new HouseInfo(true, n, null, null, t0);
                }
            }

            return new HouseInfo(false, null, null, null, null);
        }

        private static bool HouseMatches(HouseInfo input, HouseInfo stored)
        {
            // If we can't parse either side -> don't block by house, only street tokens will decide
            if (!input.HasAny || !stored.HasAny) return true;

            // Input single
            if (input.SingleNumber.HasValue)
            {
                int n = input.SingleNumber.Value;

                // Stored range
                if (stored.RangeFrom.HasValue && stored.RangeTo.HasValue)
                    return n >= stored.RangeFrom.Value && n <= stored.RangeTo.Value;

                // Stored single
                if (stored.SingleNumber.HasValue)
                    return n == stored.SingleNumber.Value;

                return true;
            }

            // Input range (rare from STT, but handle anyway)
            if (input.RangeFrom.HasValue && input.RangeTo.HasValue)
            {
                int from = input.RangeFrom.Value;
                int to = input.RangeTo.Value;

                if (stored.SingleNumber.HasValue)
                    return stored.SingleNumber.Value >= from && stored.SingleNumber.Value <= to;

                if (stored.RangeFrom.HasValue && stored.RangeTo.HasValue)
                {
                    // overlap check
                    return !(to < stored.RangeFrom.Value || from > stored.RangeTo.Value);
                }

                return true;
            }

            return true;
        }

        private static HashSet<string> GetStreetTokens(string normalized)
        {
            // remove generic words and house-like tokens to focus on street name
            var stop = new HashSet<string> { "вул", "просп", "пров", "пл", "бул", "буд", "будинок", "кв", "квартира", "підїзд", "підїзд" };

            var tokens = normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length >= 2)
                .Where(t => !stop.Contains(t))
                .Where(t => !t.Any(char.IsDigit)) // exclude house tokens
                .ToHashSet();

            return tokens;
        }

        private static bool IsAddressClose(string inputNorm, string storedNorm)
        {
            if (string.IsNullOrWhiteSpace(inputNorm) || string.IsNullOrWhiteSpace(storedNorm)) return false;

            var inputHouse = ParseHouse(inputNorm);
            var storedHouse = ParseHouse(storedNorm);

            // street similarity
            var st1 = GetStreetTokens(inputNorm);
            var st2 = GetStreetTokens(storedNorm);

            // require at least 1 common street token (you can bump to 2 if false positives appear)
            int commonStreet = st1.Intersect(st2).Count();
            if (commonStreet < 1)
                return false;

            // house logic with range support
            if (!HouseMatches(inputHouse, storedHouse))
                return false;

            return true;
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
