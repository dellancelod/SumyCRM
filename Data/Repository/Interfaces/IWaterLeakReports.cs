using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IWaterLeakReports
    {
        IQueryable<WaterLeakReport> GetWaterLeakReports();
        Task<WaterLeakReport> GetWaterLeakReportByIdAsync(Guid id);
        Task SaveWaterLeakReportAsync(WaterLeakReport entity);
        Task DeleteWaterLeakReportAsync(Guid id);
        Task DeleteWaterLeakReportAsync(WaterLeakReport report);
    }
}
