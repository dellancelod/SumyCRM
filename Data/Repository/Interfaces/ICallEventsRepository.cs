using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface ICallEventsRepository
    {
        IQueryable<CallEvent> GetCallEvents();
        Task<CallEvent> GetCallEventByIdAsync(Guid id);
        Task SaveCallEventAsync(CallEvent entity);
        Task DeleteCallEventAsync(Guid id);
        Task DeleteCallEventAsync(CallEvent log);
    }
}
