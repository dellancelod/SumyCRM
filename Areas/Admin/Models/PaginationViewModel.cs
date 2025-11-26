namespace SumyCRM.Areas.Admin.Models
{
    public class PaginationViewModel<T>
    {
        public IEnumerable<T> PageItems { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }
}
