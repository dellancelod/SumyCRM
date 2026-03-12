using SumyCRM.Models;

namespace SumyCRM.Services
{
    public interface IRequestEventService
    {
        Task SyncFromRequestAsync(Request request, CancellationToken ct = default);
    }
}
