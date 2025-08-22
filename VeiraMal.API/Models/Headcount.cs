namespace VeiraMal.API.Models
{
    public class Headcount
    {
        public int Id { get; set; }
        public int PersonnelNumber { get; set; }
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public int AgeOfEmployee { get; set; }
        public string? GenderKey { get; set; }
        public string? Country { get; set; }
        public string? PersonnelSubarea { get; set; }
        public string? Lv { get; set; }
        public string? PersonnelArea { get; set; }
        public string? EmployeeSubgroup { get; set; }
        public string? NameOfSuperior { get; set; }
        public DateTime Date { get; set; }
        public string? OrganizationalKey { get; set; }
        public string? OrganizationalUnit { get; set; }
        public string? EmployeeGroup { get; set; }
        public string? WeeklyHours { get; set; }
        public string? EmploymentPercentage { get; set; }
        public string? PositionNumber { get; set; }
        public string? PositionTitle { get; set; }
        public string? CostCentreNumber { get; set; }
        public string? CostCentreDescription { get; set; }
        public string? SalariedOrWaged { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? Tenure { get; set; }
        public string? Month { get; set; }
        public string? BusinessUnit { get; set; }
    }
}