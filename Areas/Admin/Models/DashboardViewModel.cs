using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Models
{
    public class DashboardViewModel
    {
        public IEnumerable<Request> Requests { get; set; }
        public int ActiveCount { get; set; }
        public int CompletedCount { get; set; }
        public List<CategoryStat> CategoryStats { get; set; } = new();
    }

    public class CategoryStat
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }
}
