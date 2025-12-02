using SumyCRM.Models;

namespace SumyCRM.Services
{
    public interface IScheduleAudioService
    {
        Task<string> GenerateAudioAsync(Schedule schedule, CancellationToken ct = default);
    }
}
