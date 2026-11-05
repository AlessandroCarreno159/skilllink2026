using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

namespace SkillLink_dotnet.Controllers;

[Authorize]
public class RealtimeController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RealtimeTokenService tokens,
    IConfiguration config) : Controller
{
    // GET /Realtime/Token -> token HMAC para el handshake de Socket.io.
    [HttpGet]
    public async Task<IActionResult> Token()
    {
        var actual = await users.GetUserAsync(User);
        if (actual is null) return Unauthorized();

        var usuario = await db.Users.AsNoTracking()
            .Include(u => u.Perfil)
            .Include(u => u.Empresa)
            .FirstOrDefaultAsync(u => u.Id == actual.Id);

        var nombre = usuario?.Empresa?.RazonSocial
                     ?? usuario?.Perfil?.NombreCompleto
                     ?? usuario?.UserName
                     ?? actual.Id;
        var rol = usuario?.Rol ?? actual.Rol;
        var token = tokens.CreateToken(actual.Id, nombre, rol);

        return Json(new
        {
            enabled = token is not null,
            token,
            userId = actual.Id,
            name = nombre,
            role = rol,
            url = config["Realtime:Url"] ?? string.Empty,
            heartbeatSeconds = 45
        });
    }
}
