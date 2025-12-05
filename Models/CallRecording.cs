namespace SumyCRM.Models
{
    public class CallRecording : EntityBase
    {
        public int CallNumber { get; set; }
        public string Caller { get; set; }
        public string AudioFilePath { get; set; }
    }
}
