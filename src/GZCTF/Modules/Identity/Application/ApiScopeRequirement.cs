using Microsoft.AspNetCore.Authorization;

namespace GZCTF.Modules.Identity.Application;

public sealed record ApiScopeRequirement(string Scope) : IAuthorizationRequirement;
