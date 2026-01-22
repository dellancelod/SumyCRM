namespace SumyCRM.Services
{
    public class TranscriptService
    {
        private static readonly string[] NoisePhrases =
{
            "дякую за перегляд",
            "дякую за увагу",
            "дякуємо за перегляд",
            "спасибо за просмотр",
            "thank you for watching"
        };

        public static string CleanTranscript(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Оригінал вирівнюємо для збереження (без \n навколо)
            var trimmedOriginal = text.Trim();

            // Нормалізований варіант для порівняння:
            // - в нижній регістр
            // - без пунктуації
            // - зі стиснутими пробілами
            var noPunct = new string(
                trimmedOriginal
                    .ToLowerInvariant()
                    .Where(c => !char.IsPunctuation(c))
                    .ToArray()
            );

            var normalized = string.Join(
                " ",
                noPunct
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            ).Trim();

            // Якщо це одна з "шумових" фраз – вважаємо тишею
            foreach (var phrase in NoisePhrases)
            {
                if (normalized == phrase ||
                    normalized.StartsWith(phrase + " "))
                {
                    return string.Empty;
                }
            }

            // Дуже короткий текст теж можна вважати шумом
            if (normalized.Length <= 3)
                return string.Empty;

            return trimmedOriginal;
        }
        public static async Task<string> ConvertToWhisperWavAsync(string inputPath, CancellationToken ct)
        {
            var outPath = Path.ChangeExtension(inputPath, ".whisper.wav");

            // 16kHz, mono, signed 16-bit PCM
            var args = $"-y -i \"{inputPath}\" -ac 1 -ar 16000 -c:a pcm_s16le \"{outPath}\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync(ct);

            if (p.ExitCode != 0)
            {
                var err = await p.StandardError.ReadToEndAsync();
                throw new Exception("ffmpeg convert failed: " + err);
            }

            return outPath;
        }
    }
}
