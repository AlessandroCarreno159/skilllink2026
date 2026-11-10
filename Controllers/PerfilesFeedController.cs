using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

/// <summary>Feed de perfiles: directorio público de personas y empresas aprobadas.</summary>
public class PerfilesFeedController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? seccion = "personas", string? q = null, string? ciudad = null, int pagina = 1)
    {
        seccion = string.Equals(seccion, "empresas", StringComparison.OrdinalIgnoreCase) ? "empresas" : "personas";
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        ciudad = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();

        const int porPagina = 12;
        var vm = new PerfilesFeedVm { Seccion = seccion, Q = q, Ciudad = ciudad };

        if (seccion == "empresas")
        {
            var query = db.Empresas
                .Where(e => e.Usuario!.Rol == Roles.Empresa
                            && e.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);
            if (q is not null)
                query = query.Where(e => e.RazonSocial.Contains(q)
                    || (e.Sector != null && e.Sector.Contains(q))
                    || (e.Descripcion != null && e.Descripcion.Contains(q)));
            if (ciudad is not null)
                query = query.Where(e => e.Ciudad == ciudad);
            vm.Total = await query.CountAsync();
            vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
            vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
            vm.Empresas = await query.OrderBy(e => e.RazonSocial)
                .Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToListAsync();
        }
        else
        {
            var query = db.Perfiles
                .Where(p => p.Usuario!.Rol == Roles.Trabajador
                            && p.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);
            if (q is not null)
                query = query.Where(p => p.NombreCompleto.Contains(q)
                    || (p.Especialidad != null && p.Especialidad.Contains(q))
                    || (p.Habilidades != null && p.Habilidades.Contains(q))
                    || (p.Descripcion != null && p.Descripcion.Contains(q)));
            if (ciudad is not null)
                query = query.Where(p => p.Ciudad == ciudad);
            vm.Total = await query.CountAsync();
            vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
            vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
            vm.Personas = await query.OrderBy(p => p.NombreCompleto)
                .Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToListAsync();
        }

        return View(vm);
    }
}
