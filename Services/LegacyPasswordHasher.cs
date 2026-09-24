using Microsoft.AspNetCore.Identity;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Services;

/// <summary>
/// Hasher compatible con los hashes bcrypt del Flask original ($2a$/$2b$).
/// Si el hash almacenado es bcrypt y verifica, devuelve SuccessRehashNeeded
/// para que Identity lo re-guarde en PBKDF2 en el próximo login.
/// Los hashes nuevos se crean con el hasher estándar de Identity.
/// </summary>
public sealed class LegacyPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private readonly PasswordHasher<ApplicationUser> _inner = new();

    public string HashPassword(ApplicationUser user, string password)
        => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (hashedPassword.StartsWith("$2a$", StringComparison.Ordinal)
            || hashedPassword.StartsWith("$2b$", StringComparison.Ordinal)
            || hashedPassword.StartsWith("$2y$", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;
            }
            catch
            {
                return PasswordVerificationResult.Failed;
            }
        }
        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
