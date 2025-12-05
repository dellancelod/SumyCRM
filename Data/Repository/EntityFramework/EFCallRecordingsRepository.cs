using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFCallRecordingsRepository : ICallRecordingsRepository
    {
        private readonly AppDbContext context;
        private readonly IWebHostEnvironment env;

        public EFCallRecordingsRepository(AppDbContext context, IWebHostEnvironment env)
        {
            this.context = context;
            this.env = env;
        }
        public async Task DeleteCallRecordingAsync(Guid id)
        {
            var callRecording = await context.CallRecordings.FirstOrDefaultAsync(r => r.Id == id);
            if (callRecording == null)
                return;

            DeleteAudioFileIfExists(callRecording.AudioFilePath);

            context.CallRecordings.Remove(callRecording);
            await context.SaveChangesAsync();
        }

        public async Task DeleteCallRecordingAsync(CallRecording callRecording)
        {
            if (callRecording == null)
                return;

            if (string.IsNullOrEmpty(callRecording.AudioFilePath))
            {
                var dbEntity = await context.Requests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == callRecording.Id);

                if (dbEntity != null)
                {
                    callRecording.AudioFilePath = dbEntity.AudioFilePath;
                }

            }

            DeleteAudioFileIfExists(callRecording.AudioFilePath);

            context.CallRecordings.Remove(callRecording);
            await context.SaveChangesAsync();
        }


        public async Task<CallRecording> GetCallRecordingByIdAsync(Guid id)
        {
            return await context.CallRecordings.FirstOrDefaultAsync(x => x.Id == id);
        }

        public IQueryable<CallRecording> GetCallRecordings()
        {
            return context.CallRecordings;
        }

        public async Task SaveCallRecordingAsync(CallRecording entity)
        {
            if (entity.Id == default)
            {
                context.Entry(entity).State = EntityState.Added;
            }
            else
            {
                context.Entry(entity).State = EntityState.Modified;
            }
            await context.SaveChangesAsync();
        }

        private void DeleteAudioFileIfExists(string audioFilePath)
        {
            if (string.IsNullOrWhiteSpace(audioFilePath))
                return;

            // audioFilePath like "/audio/file.wav"
            var relative = audioFilePath.TrimStart('/', '\\');
            var fullPath = Path.Combine(env.WebRootPath, relative);

            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch
                {
                    // лог при желании, но не ломаем удаление из БД
                }
            }
        }
    }
}
