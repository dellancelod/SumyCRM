namespace SumyCRM.Services
{
    public class AudioProcessing
    {
        public static string PreprocessAudio(string folder, string fileName, string fullPath)
        {
            string cleanedPath = Path.Combine(folder, "cleaned_" + fileName);

            // Спроба №1: full processing (loudnorm + denoise + compressor)
            string[] attempts =
            {
                $"-y -i \"{fullPath}\" -ac 1 -ar 16000 -filter:a \"loudnorm,afftdn,acompressor\" \"{cleanedPath}\"",

                // Спроба №2: просте перетворення в PCM 16 kHz mono (як запасний варіант)
                $"-y -i \"{fullPath}\" -ac 1 -ar 16000 -sample_fmt s16 \"{cleanedPath}\""
            };

            bool success = false;
            string ffmpegOutput = "";

            foreach (var args in attempts)
            {
                var psi = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    ffmpegOutput = proc.StandardError.ReadToEnd() + "\n" +
                                   proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                }

                if (System.IO.File.Exists(cleanedPath))
                {
                    success = true;
                    break;
                }
            }

            // Записати лог FFmpeg, раптом що
            System.IO.File.WriteAllText(
                Path.Combine(folder, "ffmpeg_last_log.txt"),
                ffmpegOutput
            );

            // Replace original file with cleaned version for Whisper
            string whisperInput = cleanedPath;

            return whisperInput;
        }
    }
}
