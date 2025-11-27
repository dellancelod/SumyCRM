using Microsoft.EntityFrameworkCore;
using OpenAI.Graders;
using SumyCRM.Models;
using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFRequestsRepository : IRequestsRepository
    {
        private readonly AppDbContext context;
        private readonly IWebHostEnvironment env;

        public EFRequestsRepository(AppDbContext context, IWebHostEnvironment env)
        {
            this.context = context;
            this.env = env;
        }
        public async Task DeleteRequestAsync(Guid id)
        {
            var request = await context.Requests.FirstOrDefaultAsync(r => r.Id == id);
            if (request == null)
                return;

            DeleteAudioFileIfExists(request.AudioFilePath);

            context.Requests.Remove(request);
            await context.SaveChangesAsync();
        }

        public async Task DeleteRequestAsync(Request request)
        {
            if (request == null)
                return;

            if (string.IsNullOrEmpty(request.AudioFilePath))
            {
                var dbEntity = await context.Requests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == request.Id);

                if (dbEntity != null)
                    request.AudioFilePath = dbEntity.AudioFilePath;
            }

            DeleteAudioFileIfExists(request.AudioFilePath);

            context.Requests.Remove(request);
            await context.SaveChangesAsync();
        }


        public async Task<Request> GetRequestByIdAsync(Guid id)
        {
            return await context.Requests.FirstOrDefaultAsync(x => x.Id == id);
        }

        public IQueryable<Request> GetRequests()
        {
            return context.Requests;
        }

        public async Task SaveRequestAsync(Request entity)
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
