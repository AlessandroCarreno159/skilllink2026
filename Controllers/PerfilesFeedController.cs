using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services.Geo;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

/// <summary>Feed de perfiles: directorio público de personas y empresas aprobadas.</summary>
public class PerfilesFeedController(ApplicationDbContext db, SkillLink.Services.AlgoliaService algolia) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? seccion = "personas", string? q = null, string? ciudad = null, int pagina = 1)
    {
        seccion = string.Equals(seccion, "empresas", StringComparison.OrdinalIgnoreCase) ? "empresas" : "personas";
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var ciudadQuery = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();
        ViewBag.CiudadExplicita = ciudadQuery is not null;

        // Respaldo por cookie de departamento cuando no hay filtro explícito.
        ciudad = ciudadQuery ?? (GeoCookies.EsTodas(Request) ? null : GeoCookies.LeerCiudad(Request));

        const int porPagina = 12;
        var vm = new PerfilesFeedVm { Seccion = seccion, Q = q, Ciudad = ciudad };
        vm.TotalPersonas = await db.Perfiles
            .CountAsync(p => p.Usuario!.Rol == Roles.Trabajador
                             && p.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);
        vm.TotalEmpresas = await db.Empresas
            .CountAsync(e => e.Usuario!.Rol == Roles.Empresa
                             && e.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);

        List<string>? idsAlgolia = null;
        if (q is not null)
        {
            // La búsqueda de perfiles usa Algolia como único motor; sin fallback LIKE a BD.
            var tipo = seccion == "empresas" ? "empresa" : "persona";
            idsAlgolia = await algolia.SearchPerfilUsuarioIdsAsync(q, tipo, 50);
            vm.UsaAlgolia = idsAlgolia is not null;
            if (idsAlgolia is null || idsAlgolia.Count == 0)
            {
                vm.SearchError = idsAlgolia is null
                    ? "La búsqueda no está disponible en este momento. Intenta nuevamente."
                    : null;
                return View(vm);
            }
        }

        if (seccion == "empresas")
        {
            var query = db.Empresas
                .Where(e => e.Usuario!.Rol == Roles.Empresa
                            && e.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);
            if (ciudad is not null)
                query = query.Where(e => e.Ciudad == ciudad);
            if (idsAlgolia is { Count: > 0 })
                query = query.Where(e => idsAlgolia.Contains(e.UsuarioId));

            if (idsAlgolia is { Count: > 0 })
            {
                var orden = idsAlgolia.Select((id, indice) => (id, indice))
                    .ToDictionary(x => x.id, x => x.indice);
                var coincidencias = (await query.ToListAsync())
                    .OrderBy(e => orden.GetValueOrDefault(e.UsuarioId, int.MaxValue))
                    .ToList();
                vm.Total = coincidencias.Count;
                vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
                vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
                vm.Empresas = coincidencias.Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToList();
            }
            else
            {
                vm.Total = await query.CountAsync();
                vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
                vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
                vm.Empresas = await query.OrderBy(e => e.RazonSocial)
                    .Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToListAsync();
            }
        }
        else
        {
            var query = db.Perfiles
                .Where(p => p.Usuario!.Rol == Roles.Trabajador
                            && p.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);
            if (ciudad is not null)
                query = query.Where(p => p.Ciudad == ciudad);
            if (idsAlgolia is { Count: > 0 })
                query = query.Where(p => idsAlgolia.Contains(p.UsuarioId));

            if (idsAlgolia is { Count: > 0 })
            {
                var orden = idsAlgolia.Select((id, indice) => (id, indice))
                    .ToDictionary(x => x.id, x => x.indice);
                var coincidencias = (await query.ToListAsync())
                    .OrderBy(p => orden.GetValueOrDefault(p.UsuarioId, int.MaxValue))
                    .ToList();
                vm.Total = coincidencias.Count;
                vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
                vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
                vm.Personas = coincidencias.Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToList();
            }
            else
            {
                vm.Total = await query.CountAsync();
                vm.TotalPaginas = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)porPagina));
                vm.Pagina = Math.Clamp(pagina, 1, vm.TotalPaginas);
                vm.Personas = await query.OrderBy(p => p.NombreCompleto)
                    .Skip((vm.Pagina - 1) * porPagina).Take(porPagina).ToListAsync();
            }
        }

        return View(vm);
    }
}
