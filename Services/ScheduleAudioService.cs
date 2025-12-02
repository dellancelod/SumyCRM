using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using SumyCRM.Models;
using System.Text.RegularExpressions;

namespace SumyCRM.Services
{
    public class ScheduleAudioService : IScheduleAudioService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _soundsPath;
        private readonly string _model;
        private readonly string _voice;
        private readonly string _prefix;

        public ScheduleAudioService(
            HttpClient httpClient,
            IConfiguration config)
        {
            _httpClient = httpClient;

            _apiKey = config["OpenAI:ApiKey"]
                      ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

            _soundsPath = config["ScheduleAudio:AsteriskSoundsPath"]
                          ?? "/usr/share/asterisk/sounds/en/";

            _model = config["ScheduleAudio:Model"] ?? "tts-1";
            _voice = config["ScheduleAudio:Voice"] ?? "alloy";
            _prefix = config["ScheduleAudio:Prefix"] ?? "schedule_";
        }

        public async Task<string> GenerateAudioAsync(Schedule schedule, CancellationToken ct = default)
        {
            // имя файла — что потом пойдёт в AudioFileName
            var safeNumber = schedule.Number
                ?.Trim()
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace("\\", "_");

            var fileNameWithoutExt = $"{_prefix}{safeNumber}_uk";
            var fullPath = Path.Combine(_soundsPath, fileNameWithoutExt + ".wav");


            // Текст, который будет озвучен
            var text = await NormalizeTextWithAIAsync($"Розклад маршруту {schedule.Number}: {schedule.Time}.");

            var body = new
            {
                model = _model,      // "tts-1"
                voice = _voice,      // "alloy"
                input = text,
                response_format = "wav"       // просим сразу wav, удобно для Asterisk :contentReference[oaicite:0]{index=0}
            };

            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/audio/speech");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Exception(
                    $"OpenAI TTS error {response.StatusCode}: {errorBody}");
            }
            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);

            Directory.CreateDirectory(_soundsPath);
            await File.WriteAllBytesAsync(fullPath, audioBytes, ct);

            return fileNameWithoutExt;
        }
        private async Task<string> NormalizeTextWithAIAsync(string rawText, CancellationToken ct = default)
        {
            // Підготовка запиту до Chat Completions
            var body = new
            {
                model = "gpt-4.1-mini",
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Ти помічник, який перетворює український текст з цифрами на природний усний варіант для озвучення. " +
                                  "Перетворюй номери маршрутів на слова у родовому відмінку (\"маршруту дванадцятого\"), " +
                                  "час на природні фрази (\"з восьмої до вісімнадцятої години\"), прибирай двокрапки та скорочення, " +
                                  "але зберігай сенс. Пиши тільки фінальний текст без пояснень."
                    },
                    new
                    {
                        role = "user",
                        content = rawText
                    }
                },
                temperature = 0.2   // ← ВАЖНО: именно "temperature = 0.2", а не просто "temperature"
            };

            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/chat/completions");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Exception($"OpenAI Chat error {response.StatusCode}: {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var content = doc
                .RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return content ?? rawText;
        }

    }
}
