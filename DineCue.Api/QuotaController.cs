using DineCue.Application;
using Microsoft.AspNetCore.Mvc;

namespace DineCue.Api;

[Route("quota")]
public sealed class QuotaController(IQuotaService quotas) : DineCueControllerBase
{
    [HttpGet]
    public Task<QuotaStateResponse> Get(CancellationToken ct) => quotas.GetAsync(UserId, ct);
}
