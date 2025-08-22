namespace Vstup_Predictor.Models
{
    public class Application
    {
        public string Id { get; set; }
        public string State { get; set; }
        public int? Priority { get; set; }
        public double Grade { get; set; }

        // Navigation
        public string PersonId { get; set; }
        public string OfferId { get; set; }
        public Person Person { get; set; }
        public Offer Offer { get; set; }
    }
}
