using System.Security.Principal;
using BifitReport.LogViewer.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Security;

public sealed class AllowedPrincipalHandler(IOptions<AccessOptions> options) : AuthorizationHandler<AllowedPrincipalRequirement>
{
    private readonly AccessOptions _options = options.Value;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedPrincipalRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return Task.CompletedTask;

        var identityName = context.User.Identity.Name;
        if (_options.AllowedPrincipals.Any(allowed => string.Equals(allowed, identityName, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        foreach (var allowed in _options.AllowedPrincipals)
        {
            try
            {
                if (!context.User.IsInRole(allowed)) continue;
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
            catch (SystemException)
            {
                // Некорректное имя или SID проверяется следующей записью allow-list.
            }
        }

        if (context.User.Identity is WindowsIdentity windowsIdentity)
        {
            var principal = new WindowsPrincipal(windowsIdentity);
            foreach (var allowed in _options.AllowedPrincipals)
            {
                try
                {
                    var matches = allowed.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                        ? principal.IsInRole(new SecurityIdentifier(allowed))
                        : principal.IsInRole(allowed);
                    if (!matches) continue;
                    context.Succeed(requirement);
                    break;
                }
                catch (SystemException)
                {
                    // Неизвестное имя или SID не даёт доступ и проверяется следующей записью.
                }
            }
        }

        return Task.CompletedTask;
    }
}
