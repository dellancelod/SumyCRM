using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SumyCRM.Data;
using SumyCRM.Models;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Audio;
using System.IO;
using static SumyCRM.Services.AudioProcessing;

namespace SumyCRM.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        private readonly string _apiKey;

        public HomeController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _apiKey = config["OpenAI:ApiKey"];
        }

        // Show all requests
        public IActionResult Index()
        {
            var list = _db.Requests.OrderByDescending(r => r.CreatedAt).ToList();
            return View(list);
        }

        // Upload page
        public IActionResult Upload()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await _db.Requests.FindAsync(id);
            if (record == null)
                return NotFound();

            // Try to delete audio file
            if (!string.IsNullOrWhiteSpace(record.AudioFilePath))
            {
                // AudioFilePath like "/audio/xxx.wav"
                var relativePath = record.AudioFilePath.TrimStart('/', '\\');
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);

                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    catch
                    {
                        // ignore file delete errors, DB delete will still happen
                    }
                }
            }

            _db.Requests.Remove(record);
            await _db.SaveChangesAsync();

            return RedirectToAction("Index");
        }
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile audio, string caller, string text)
        {

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

            string whisperInput = PreprocessAudio(folder, fileName, fullPath);

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
                     await audioClient.TranscribeAudioAsync(whisperInput, options);
            string transcript = transcription.Text ?? "(empty)"; 
            
            // ===== Save in DB =====
            var record = new Request 
            { 
                Caller = caller, 
                Text = text,
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
