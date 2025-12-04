using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data
{
    public class DataManager
    {
        public IRequestsRepository Requests { get; set; }
        public ICategoriesRepository Categories { get; set; }
        public ISchedulesRepository Schedules { get; set; }
        public IFacilitiesRepository Facilities { get; set; }
        public IUserCategories UserCategories { get; set; }


        public DataManager(IRequestsRepository requests, ICategoriesRepository categories,
            ISchedulesRepository schedules, IFacilitiesRepository facilities, 
            IUserCategories userCategories) { 
            Requests = requests;
            Categories = categories;
            Schedules = schedules;
            Facilities = facilities;
            UserCategories = userCategories;
        }
    }
}
