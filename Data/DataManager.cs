using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data
{
    public class DataManager
    {
        public IRequestsRepository Requests { get; set; }
        public ICategoriesRepository Categories { get; set; }

        public DataManager(IRequestsRepository requests, ICategoriesRepository categories) { 
            Requests = requests;
            Categories = categories;
        }
    }
}
