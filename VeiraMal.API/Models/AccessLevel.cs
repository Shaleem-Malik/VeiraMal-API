namespace VeiraMal.API.Models
{
    public class AccessLevel
    {
        public int AccessLevelId { get; set; }
        public Guid CompanyId { get; set; }
        public string Name { get; set; } = null!;
    }
}
