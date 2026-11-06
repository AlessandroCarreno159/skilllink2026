using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Filters;

/// <summary>
/// Replica cuenta_activa_required de Flask: exige EstadoCuenta == aprobado (admin bypass).
/// </summary>
public sealed class CuentaActivaAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }
        if (user.IsInRole(Roles.Admin)) return;
        var estado = user.FindFirst("EstadoCuenta")?.Value;
        if (!string.Equals(estado, EstadosCuenta.Aprobado, StringComparison.OrdinalIgnoreCase))
        {
            var controller = (Controller)context.Controller;
            controller.TempData["Warning"] = $"Su cuenta no está activa. Estado: {estado}.";
            context.Result = new RedirectToActionResult("Index", "Dashboard", null);
        }
    }
}
