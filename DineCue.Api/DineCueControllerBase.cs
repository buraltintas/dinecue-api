using DineCue.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineCue.Api;

[ApiController]
[Authorize]
public abstract class DineCueControllerBase : ControllerBase
{
    protected Guid UserId => User.GetUserId();
}
