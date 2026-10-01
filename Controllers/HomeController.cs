using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using System.Diagnostics;

namespace SkillLink_dotnet.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var pubs = await db.Publicaciones
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => p.Activa).OrderByDescending(p => p.FechaCreacion).Take(6).ToListAsync();
        var ids = pubs.Select(p => p.Id).ToList();
        ViewBag.CommentsCount = await db.ComentariosHilo
            .Where(c => ids.Contains(c.PublicacionId) && c.Activo)
            .GroupBy(c => c.PublicacionId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var autorIds = pubs.Select(p => p.AutorId).Distinct().ToList();
        _ = autorIds; // Reputación por usuario congelada; ahora se califica por publicación.
        ViewBag.Ratings = await db.Valoraciones
            .Where(v => v.PublicacionId != null && ids.Contains(v.PublicacionId.Value) && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId!.Value)
            .Select(g => new { PublicacionId = g.Key, Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .ToDictionaryAsync(x => x.PublicacionId, x => (Promedio: x.Promedio, Total: x.Total));
        return View(pubs);
    }

    [HttpGet]
    public async Task<IActionResult> Buscar(string modo = "ofertas", string? q = null, string? ciudad = null, string? tipo = null, string? modalidad = null, string? categoria = null, int? estrellas = null, string? opCalif = ">=")
    {
        var query = db.Publicaciones
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => p.Activa).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => (p.Titulo + " " + p.Contenido + " " + (p.HabilidadesReq ?? "")).Contains(q));
        if (!string.IsNullOrWhiteSpace(ciudad))
            query = query.Where(p => p.Autor!.Perfil!.Ciudad == ciudad || p.Autor!.Empresa!.Ciudad == ciudad);
        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(p => p.Tipo == tipo);
        if (!string.IsNullOrWhiteSpace(modalidad))
            query = query.Where(p => p.Modalidad == modalidad);
        if (!string.IsNullOrWhiteSpace(categoria))
            query = query.Where(p => p.Categoria == categoria);
        if (estrellas is >= 1 and <= 5)
        {
            var op = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
            var e = estrellas.Value;
            var promedios = db.Valoraciones
                .Where(v => v.PublicacionId != null && v.Activo && v.Calificacion != null)
                .GroupBy(v => v.PublicacionId!.Value)
                .Select(g => new { Id = g.Key, Prom = g.Average(v => v.Calificacion!.Value) });
            promedios = op switch
            {
                "<=" => promedios.Where(x => x.Prom <= e),
                "=" => e >= 5
                    ? promedios.Where(x => x.Prom == 5)
                    : promedios.Where(x => x.Prom >= e && x.Prom < e + 1),
                _ => promedios.Where(x => x.Prom >= e),
            };
            query = query.Where(p => promedios.Select(x => x.Id).Contains(p.Id));
        }
        var pubs = await query.OrderByDescending(p => p.FechaCreacion).Take(50).ToListAsync();
        var ids = pubs.Select(p => p.Id).ToList();
        ViewBag.CommentsCount = await db.ComentariosHilo
            .Where(c => ids.Contains(c.PublicacionId) && c.Activo)
            .GroupBy(c => c.PublicacionId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var autorIds = pubs.Select(p => p.AutorId).Distinct().ToList();
        _ = autorIds; // Reputación por usuario congelada; ahora se califica por publicación.
        ViewBag.Ratings = await db.Valoraciones
            .Where(v => v.PublicacionId != null && ids.Contains(v.PublicacionId.Value) && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId!.Value)
            .Select(g => new { PublicacionId = g.Key, Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .ToDictionaryAsync(x => x.PublicacionId, x => (Promedio: x.Promedio, Total: x.Total));
        ViewBag.Modo = modo; ViewBag.Q = q; ViewBag.Ciudad = ciudad; ViewBag.Tipo = tipo;
        ViewBag.Modalidad = modalidad; ViewBag.Categoria = categoria;
        ViewBag.Estrellas = estrellas is >= 1 and <= 5 ? estrellas : null;
        ViewBag.OpCalif = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
        return View(pubs);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
