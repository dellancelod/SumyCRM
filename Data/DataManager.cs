using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data
{
    public class DataManager
    {
        public IRequestsRepository Requests { get; set; }

        public DataManager(IRequestsRepository requests) { 
            Requests = requests;
        }
    }
}
