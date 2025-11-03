using System.Collections.Generic;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;

namespace VeiraMal.API.Services.Interfaces
{
    public interface ISubscriptionService
    {
        Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync();
    }
}
