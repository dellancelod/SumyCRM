using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data
{
    public class DataManager
    {
        public IRequestsRepository Requests { get; set; }
        public ICategoriesRepository Categories { get; set; }
        public ISchedulesRepository Schedules { get; set; }
        public IFacilitiesRepository Facilities { get; set; }
        public IUserFacilities UserFacilities { get; set; }
        public ICallRecordingsRepository CallRecordings { get; set; }
        public IWaterLeakReports WaterLeakReports { get; set; }


        public DataManager(IRequestsRepository requests, ICategoriesRepository categories,
            ISchedulesRepository schedules, IFacilitiesRepository facilities, 
            IUserFacilities userFacilities, ICallRecordingsRepository callRecordings,
            IWaterLeakReports waterLeakReports) { 
            Requests = requests;
            Categories = categories;
            Schedules = schedules;
            Facilities = facilities;
            UserFacilities = userFacilities;
            CallRecordings = callRecordings;
            WaterLeakReports = waterLeakReports;
        }
    }
}
