using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Manoir.HomeDocs.Api.Security;

public sealed class PublicPinAuthorization
{
    public const string Scheme = "HomeDocs.PublicPin";
    public const string ContextClaim = "homedocs:public-context";
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public async Task<bool> HasContextGrantAsync(HttpContext context, string contextId)
    {
        var result = await context.AuthenticateAsync(Scheme);
        if (!result.Succeeded || result.Principal is null) return false;

        var grantedContext = result.Principal.FindFirstValue(ContextClaim);
        if (!StringComparer.Ordinal.Equals(grantedContext, contextId))
        {
            await context.SignOutAsync(Scheme);
            return false;
        }

        await context.SignInAsync(Scheme, result.Principal, NewProperties());
        return true;
    }

    public Task GrantAsync(HttpContext context, string contextId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ContextClaim, contextId)],
            Scheme);
        return context.SignInAsync(Scheme, new ClaimsPrincipal(identity), NewProperties());
    }

    public Task RevokeAsync(HttpContext context) => context.SignOutAsync(Scheme);

    private static AuthenticationProperties NewProperties() => new()
    {
        IsPersistent = false,
        AllowRefresh = false,
        IssuedUtc = DateTimeOffset.UtcNow,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(IdleTimeout)
    };
}
