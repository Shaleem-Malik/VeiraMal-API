namespace VeiraMal.API.Models
{
    public class BusinessUnit
    {
        public int BusinessUnitId { get; set; }
        public Guid CompanyId { get; set; }
        public string Name { get; set; } = null!;
    }
}
