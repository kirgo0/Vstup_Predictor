namespace Vstup_Predictor.Models
{
    public class University
    {
        public string Id { get; set; }
        public string CityId { get; set; }
        public string Name { get; set; }
        public IEnumerable<Offer> Offers { get; set; } = new List<Offer>();
        public string RequestParameter { get; set; }
    }
}
