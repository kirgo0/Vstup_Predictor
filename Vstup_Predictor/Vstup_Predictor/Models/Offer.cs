namespace Vstup_Predictor.Models
{
    public class Offer
    {
        public string Id { get; set; }
        public string UniversityId { get; set; }
        public string? Speciality { get; set; }
        public string? Program { get; set; }
        public int? BudgetCount { get; set; }
        public IEnumerable<Application> Applications { get; set; } = new List<Application>();
        public string? RequestParameter { get; set; }
    }
}
