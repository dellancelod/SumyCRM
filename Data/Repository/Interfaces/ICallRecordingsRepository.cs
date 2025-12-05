using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface ICallRecordingsRepository
    {
        IQueryable<CallRecording> GetCallRecordings();
        Task<CallRecording> GetCallRecordingByIdAsync(Guid id);
        Task SaveCallRecordingAsync(CallRecording entity);
        Task DeleteCallRecordingAsync(Guid id);
        Task DeleteCallRecordingAsync(CallRecording request);
    }
}
