using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFWaterLeakReportsRepository : IWaterLeakReports
    {
        private readonly AppDbContext context;
        public EFWaterLeakReportsRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task DeleteWaterLeakReportAsync(Guid id)
        {
            var waterLeak = await context.WaterLeakReports.FirstOrDefaultAsync(x => x.Id == id);
            if (waterLeak == null) return;

            context.WaterLeakReports.Remove(waterLeak);
            await context.SaveChangesAsync();
        }

        public async Task DeleteWaterLeakReportAsync(WaterLeakReport report)
        {
            context.WaterLeakReports.Remove(report);
            await context.SaveChangesAsync();
        }

        public IQueryable<WaterLeakReport> GetWaterLeakReports()
        {
            return context.WaterLeakReports;
        }

        public async Task<WaterLeakReport> GetWaterLeakReportByIdAsync(Guid id)
        {
            return await context.WaterLeakReports.FirstOrDefaultAsync(x => x.Id == id);
        }
        public async Task SaveWaterLeakReportAsync(WaterLeakReport entity)
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
    }
}
