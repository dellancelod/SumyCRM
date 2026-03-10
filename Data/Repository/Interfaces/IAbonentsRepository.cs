using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IAbonentsRepository
    {
        IQueryable<Abonent> GetAbonents();
        Task<Abonent> GetAbonentByIdAsync(Guid id);
        Task SaveAbonentAsync(Abonent entity);
        Task DeleteAbonentAsync(Guid id);
        Task DeleteAbonentAsync(Abonent abonent);
    }
}
