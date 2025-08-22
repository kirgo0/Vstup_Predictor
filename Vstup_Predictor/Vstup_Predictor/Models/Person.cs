namespace Vstup_Predictor.Models
{
    public class Person
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string TestResults { get; set; }
        public IEnumerable<Application> Applications { get; set; }
    }
}
