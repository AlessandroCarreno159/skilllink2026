using Microsoft.AspNetCore.Identity;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Data;

/// <summary>
/// Crea roles y el admin inicial desde configuración (sin secretos en código).
/// Equivale a DEFAULT_ADMINS de database.py.
/// </summary>
public static class IdentitySeed
{
    public static async Task EnsureAsync(IServiceProvider sp, IConfiguration config)
    {
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in new[] { Roles.Trabajador, Roles.Empresa, Roles.Admin })
            if (!await roles.RoleExistsAsync(r))
                await roles.CreateAsync(new IdentityRole(r));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = config["AdminSeed:Email"] ?? "admin@skilllink.local";
        var dni = config["AdminSeed:Dni"] ?? "10000001";
        var pass = config["AdminSeed:Password"];
        if (string.IsNullOrWhiteSpace(pass)) return; // no se crea admin sin password configurado

        var existing = await users.FindByNameAsync(dni);
        if (existing is not null) return;
        var admin = new ApplicationUser
        {
            UserName = dni, Dni = dni, Email = email,
            Rol = Roles.Admin, EstadoCuenta = EstadosCuenta.Aprobado,
            PerfilValidado = true, EmailConfirmed = true
        };
        var result = await users.CreateAsync(admin, pass);
        if (result.Succeeded)
            await users.AddToRoleAsync(admin, Roles.Admin);
    }
}
