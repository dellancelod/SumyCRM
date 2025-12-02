using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface ISchedulesRepository
    {
        IQueryable<Schedule> GetSchedules();
        Task<Schedule> GetScheduleByIdAsync(Guid id);
        Task SaveScheduleAsync(Schedule entity);
        Task DeleteScheduleAsync(Guid id);
        Task DeleteScheduleAsync(Schedule request);
        bool IsUniqueScheduleNumber(string number, Guid? id = null);
    }
}
