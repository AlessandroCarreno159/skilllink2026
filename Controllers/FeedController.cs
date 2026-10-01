using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Filters;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

/// <summary>Feed laboral: listar / nueva / detalle / comentar (feed.py).</summary>
public class FeedController(ApplicationDbContext db, UserManager<ApplicationUser> users, ImageUploadService uploads) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? seccion = "laboral", string? tipo = null, string? q = null, string? ciudad = null, string? modalidad = null, string? categoria = null, int? estrellas = null, string? opCalif = ">=", int pagina = 1)
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
        const int porPagina = 10;
        var total = await query.CountAsync();
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)porPagina));
        pagina = Math.Clamp(pagina, 1, totalPaginas);
        var pubs = await query.OrderByDescending(p => p.FechaCreacion)
            .Skip((pagina - 1) * porPagina).Take(porPagina).ToListAsync();
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
        ViewBag.Seccion = seccion; ViewBag.Q = q; ViewBag.Ciudad = ciudad; ViewBag.Tipo = tipo;
        ViewBag.Modalidad = modalidad; ViewBag.Categoria = categoria;
        ViewBag.Estrellas = estrellas is >= 1 and <= 5 ? estrellas : null;
        ViewBag.OpCalif = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
        ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas; ViewBag.Total = total;
        return View(pubs);
    }

    [HttpGet]
    [Authorize]
    [Authorize(Roles = $"{Roles.Trabajador},{Roles.Empresa}")]
    public IActionResult Create() => View(new PublicacionCreateVm());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    [CuentaActiva]
    [Authorize(Roles = $"{Roles.Trabajador},{Roles.Empresa}")]
    public async Task<IActionResult> Create(PublicacionCreateVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var yo = (await users.GetUserAsync(User))!;
        string? imagen = null;
        if (vm.Imagen is not null && vm.Imagen.Length > 0)
        {
            var error = ImageUploadService.Validar(vm.Imagen);
            if (error is not null) { ModelState.AddModelError(nameof(vm.Imagen), error); return View(vm); }
            imagen = await uploads.GuardarAsync(vm.Imagen, "publicaciones");
        }
        db.Publicaciones.Add(new Publicacion
        {
            AutorId = yo.Id, Tipo = vm.Tipo, Titulo = vm.Titulo,
            Contenido = vm.Contenido, HabilidadesReq = vm.HabilidadesReq, Imagen = imagen,
            Categoria = string.IsNullOrWhiteSpace(vm.Categoria) ? null : vm.Categoria,
            Modalidad = string.IsNullOrWhiteSpace(vm.Modalidad) ? null : vm.Modalidad,
            PrecioTexto = string.IsNullOrWhiteSpace(vm.PrecioTexto) || vm.PrecioTexto.Trim() == "A convenir" ? null : vm.PrecioTexto.Trim(),
            Vacantes = vm.Vacantes
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Publicación creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var p = await db.Publicaciones
            .Include(x => x.Autor).ThenInclude(a => a!.Perfil)
            .Include(x => x.Autor).ThenInclude(a => a!.Empresa)
            .FirstOrDefaultAsync(x => x.Id == id && x.Activa);
        if (p is null) return NotFound();
        ViewBag.Comentarios = await db.ComentariosHilo
            .Include(c => c.Autor).ThenInclude(a => a!.Perfil)
            .Include(c => c.Autor).ThenInclude(a => a!.Empresa)
            .Where(c => c.PublicacionId == id && c.Activo && c.ParentId == null)
            .OrderByDescending(c => c.Fecha).Take(5).ToListAsync();
        ViewBag.TotalComentarios = await db.ComentariosHilo
            .CountAsync(c => c.PublicacionId == id && c.Activo);
        var rating = await db.Valoraciones
            .Where(v => v.PublicacionId == id && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId)
            .Select(g => new { Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .FirstOrDefaultAsync();
        ViewBag.RatingPromedio = rating?.Promedio ?? 0;
        ViewBag.RatingTotal = rating?.Total ?? 0;
        int? miVoto = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var yoId = users.GetUserId(User);
            if (yoId is not null)
                miVoto = await db.Valoraciones
                    .Where(v => v.PublicacionId == id && v.AutorId == yoId && v.Activo)
                    .Select(v => v.Calificacion)
                    .FirstOrDefaultAsync();
        }
        ViewBag.MiCalificacion = miVoto;
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> Calificar(int id, int calificacion)
    {
        if (calificacion < 1 || calificacion > 5)
        {
            TempData["Error"] = "Calificación inválida (1 a 5 estrellas).";
            return RedirectToAction(nameof(Details), new { id });
        }
        var p = await db.Publicaciones.FirstOrDefaultAsync(x => x.Id == id && x.Activa);
        if (p is null) return NotFound();
        var yo = (await users.GetUserAsync(User))!;
        if (p.AutorId == yo.Id)
        {
            TempData["Warning"] = "No puedes calificar tu propia publicación.";
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            var existente = await db.Valoraciones
                .FirstOrDefaultAsync(v => v.PublicacionId == id && v.AutorId == yo.Id);
            if (existente is null)
            {
                db.Valoraciones.Add(new ComentarioValoracion
                {
                    AutorId = yo.Id,
                    DestinatarioId = p.AutorId,
                    PublicacionId = id,
                    Calificacion = calificacion,
                    Comentario = string.Empty,
                    Activo = true
                });
                TempData["Success"] = "Calificación guardada.";
            }
            else
            {
                existente.Calificacion = calificacion;
                existente.Activo = true;
                existente.Fecha = DateTime.UtcNow;
                TempData["Success"] = "Calificación actualizada.";
            }
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Ya registraste tu calificación para esta publicación.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> Comment(int id, string comentario, int? parentId = null)
    {
        var yo = (await users.GetUserAsync(User))!;
        if (string.IsNullOrWhiteSpace(comentario)) { TempData["Warning"] = "Comentario vacío."; return RedirectToAction(nameof(Details), new { id }); }
        db.ComentariosHilo.Add(new ComentarioPublicacion
        { PublicacionId = id, AutorId = yo.Id, Comentario = comentario.Trim(), ParentId = parentId });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }
}
