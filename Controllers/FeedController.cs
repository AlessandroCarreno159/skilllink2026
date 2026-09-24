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
    public async Task<IActionResult> Index(string? seccion = "laboral", string? tipo = null, string? q = null, string? ciudad = null, string? modalidad = null, string? categoria = null, int pagina = 1)
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
        ViewBag.Rep = await db.Reputaciones
            .Where(r => autorIds.Contains(r.UsuarioId))
            .ToDictionaryAsync(r => r.UsuarioId, r => r);
        ViewBag.Seccion = seccion; ViewBag.Q = q; ViewBag.Ciudad = ciudad; ViewBag.Tipo = tipo;
        ViewBag.Modalidad = modalidad; ViewBag.Categoria = categoria;
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
            PrecioTexto = string.IsNullOrWhiteSpace(vm.PrecioTexto) ? null : vm.PrecioTexto.Trim(),
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
        ViewBag.Reputacion = await db.Reputaciones.FindAsync(p.AutorId);
        return View(p);
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
