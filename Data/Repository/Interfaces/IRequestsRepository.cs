using OpenAI.Graders;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IRequestsRepository
    {
        IQueryable<Request> GetRequests();
        Task<Request> GetRequestByIdAsync(Guid id);
        Task SaveRequestAsync(Request entity);
        Task DeleteRequestAsync(Guid id);
        Task DeleteRequestAsync(Request request);
    }
}
