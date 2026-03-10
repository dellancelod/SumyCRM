using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFAbonentsRepository : IAbonentsRepository
    {
        private readonly AppDbContext context;
        public EFAbonentsRepository(AppDbContext context)
        {
            this.context = context;
        }
        public async Task DeleteAbonentAsync(Guid id)
        {
            var abonent = await context.Abonents.FirstOrDefaultAsync(x => x.Id == id);


            context.Abonents.Remove(abonent);
            await context.SaveChangesAsync();
        }

        public async Task DeleteAbonentAsync(Abonent abonent)
        {
            context.Abonents.Remove(abonent);
            await context.SaveChangesAsync();
        }

        public async Task<Abonent> GetAbonentByIdAsync(Guid id)
        {
            return await context.Abonents.FirstOrDefaultAsync(x => x.Id == id);
        }

        public IQueryable<Abonent> GetAbonents()
        {
            return context.Abonents;
        }

        public async Task SaveAbonentAsync(Abonent entity)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            var existing = await context.Abonents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == entity.Id);

            if (existing == null)
                await context.Abonents.AddAsync(entity);
            else
                context.Abonents.Update(entity);

            await context.SaveChangesAsync();
        }

    }
}
