using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Models
{
    public class DashboardViewModel
    {
        public IEnumerable<Request> Requests { get; set; }
        public int ActiveCount { get; set; }
        public int CompletedCount { get; set; }
    }
}
