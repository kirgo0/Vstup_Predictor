namespace Vstup_Predictor.Models
{
    public class City
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public IEnumerable<University> Universities { get; set; } = new List<University>();
        public string? RequestParameter { get; set; }
    }
}
