using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.DTOs
{
    public class AssignSuperUsersDto
    {
        [Required]
        public Guid SubCompanyId { get; set; }

        // user ids (parent-company superusers) to assign (replace existing assignments if you want)
        public int[] UserIds { get; set; } = Array.Empty<int>();
    }
}