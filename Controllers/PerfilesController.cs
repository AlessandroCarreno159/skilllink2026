using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

[Authorize]
public class WorkerController(ApplicationDbContext db, UserManager<ApplicationUser> users, ImageUploadService uploads) : Controller
{
    [HttpGet("/trabajador/registro/{userId}")]
    [AllowAnonymous]
    public IActionResult Register(string userId) => View(new TrabajadorRegistroVm());

    [HttpPost("/trabajador/registro/{userId}"), ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string userId, TrabajadorRegistroVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var u = await users.FindByIdAsync(userId);
        if (u is null) return NotFound();
        string? foto = null, cv = null;
        if (vm.Foto is { Length: > 0 })
        {
            var error = ImageUploadService.Validar(vm.Foto);
            if (error is not null) { ModelState.AddModelError(nameof(vm.Foto), error); return View(vm); }
            foto = await uploads.GuardarAsync(vm.Foto, "fotos");
        }
        if (vm.DocumentoExperiencia is { Length: > 0 })
        {
            if (vm.DocumentoExperiencia.Length > 10 * 1024 * 1024)
            { ModelState.AddModelError(nameof(vm.DocumentoExperiencia), "El documento supera el máximo de 10 MB."); return View(vm); }
            var dir = Path.Combine("wwwroot", "uploads", "cv");
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid()}{Path.GetExtension(vm.DocumentoExperiencia.FileName).ToLowerInvariant()}";
            await using var fs = System.IO.File.Create(Path.Combine(dir, name));
            await vm.DocumentoExperiencia.CopyToAsync(fs);
            cv = $"cv/{name}";
        }
        db.Perfiles.Add(new Perfil
        {
            UsuarioId = u.Id, NombreCompleto = vm.NombreCompleto, Ciudad = vm.Ciudad,
            Telefono = vm.Telefono, Especialidad = vm.Especialidad,
            Descripcion = vm.Descripcion, Habilidades = vm.Habilidades,
            Disponibilidad = string.IsNullOrWhiteSpace(vm.Disponibilidad) ? "disponible" : vm.Disponibilidad!,
            Foto = foto, CvPdf = cv
        });
        if (cv is not null)
            db.Documentos.Add(new DocumentoAcreditacion
            { UsuarioId = u.Id, Archivo = cv, NombreOriginal = vm.DocumentoExperiencia!.FileName });
        db.Antecedentes.Add(new Antecedente
        {
            UsuarioId = u.Id, TieneAntecedentes = vm.TieneAntecedentes,
            Declaracion = vm.DeclaracionAntecedentes
        });
        u.EstadoCuenta = EstadosCuenta.Pendiente;
        await db.SaveChangesAsync();
        TempData["Success"] = "Perfil enviado. Pendiente de aprobación.";
        return RedirectToAction("Login", "Account");
    }

    [HttpGet, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> Perfil()
    {
        var yo = (await users.GetUserAsync(User))!;
        ViewBag.Perfil = await db.Perfiles.FirstOrDefaultAsync(p => p.UsuarioId == yo.Id);
        ViewBag.Experiencias = await db.Experiencias.Where(e => e.UsuarioId == yo.Id).ToListAsync();
        ViewBag.Documentos = await db.Documentos.Where(d => d.UsuarioId == yo.Id)
            .OrderByDescending(d => d.FechaSubida).ToListAsync();
        return View();
    }

    // Perfil público de cualquier trabajador (avatar/nombre clicables).
    [HttpGet("/trabajador/perfil/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Publico(string id)
    {
        var u = await db.Users.Include(x => x.Perfil).FirstOrDefaultAsync(x => x.Id == id && x.Rol == Roles.Trabajador);
        if (u?.Perfil is null) return NotFound();
        ViewBag.Perfil = u.Perfil;
        ViewBag.Usuario = u;
        ViewBag.Experiencias = await db.Experiencias.Where(e => e.UsuarioId == id).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> SubirFoto(IFormFile? foto)
    {
        var error = ImageUploadService.Validar(foto);
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Perfil)); }
        var yo = (await users.GetUserAsync(User))!;
        var perfil = await db.Perfiles.FirstOrDefaultAsync(p => p.UsuarioId == yo.Id);
        if (perfil is null) { TempData["Error"] = "Aún no tiene perfil."; return RedirectToAction(nameof(Perfil)); }
        var anterior = perfil.Foto;
        perfil.Foto = await uploads.GuardarAsync(foto!, "fotos");
        await db.SaveChangesAsync();
        uploads.Borrar(anterior);
        TempData["Success"] = "Foto de perfil actualizada.";
        return RedirectToAction(nameof(Perfil));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> GuardarDescripcion(string? descripcion)
    {
        var yo = (await users.GetUserAsync(User))!;
        var perfil = await db.Perfiles.FirstOrDefaultAsync(p => p.UsuarioId == yo.Id);
        if (perfil is null) { TempData["Error"] = "Aún no tiene perfil."; return RedirectToAction(nameof(Perfil)); }
        perfil.Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        await db.SaveChangesAsync();
        TempData["Success"] = "Descripción actualizada.";
        return RedirectToAction(nameof(Perfil));
    }

    // Editar datos adjuntos (todo menos el nombre, reservado al admin).
    [HttpGet, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> EditarDatos()
    {
        var yo = (await users.GetUserAsync(User))!;
        var perfil = await db.Perfiles.FirstOrDefaultAsync(p => p.UsuarioId == yo.Id);
        if (perfil is null) { TempData["Error"] = "Aún no tiene perfil."; return RedirectToAction(nameof(Perfil)); }
        return View(new TrabajadorDatosVm
        {
            Ciudad = perfil.Ciudad, Telefono = perfil.Telefono,
            Especialidad = perfil.Especialidad, Descripcion = perfil.Descripcion,
            Habilidades = perfil.Habilidades, Disponibilidad = perfil.Disponibilidad
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> EditarDatos(TrabajadorDatosVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var yo = (await users.GetUserAsync(User))!;
        var perfil = await db.Perfiles.FirstOrDefaultAsync(p => p.UsuarioId == yo.Id);
        if (perfil is null) { TempData["Error"] = "Aún no tiene perfil."; return RedirectToAction(nameof(Perfil)); }
        perfil.Ciudad = vm.Ciudad; perfil.Telefono = vm.Telefono;
        perfil.Especialidad = vm.Especialidad; perfil.Habilidades = vm.Habilidades;
        perfil.Disponibilidad = vm.Disponibilidad;
        perfil.Descripcion = string.IsNullOrWhiteSpace(vm.Descripcion) ? null : vm.Descripcion.Trim();
        await db.SaveChangesAsync();
        TempData["Success"] = "Datos actualizados.";
        return RedirectToAction(nameof(Perfil));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> SubirDocumento(IFormFile? documento)
    {
        var error = ImageUploadService.ValidarDocumento(documento);
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Perfil)); }
        var yo = (await users.GetUserAsync(User))!;
        var archivo = await uploads.GuardarAsync(documento!, "acreditacion");
        db.Documentos.Add(new DocumentoAcreditacion
        { UsuarioId = yo.Id, Archivo = archivo, NombreOriginal = documento!.FileName });
        await db.SaveChangesAsync();
        TempData["Success"] = "Documento de acreditación subido.";
        return RedirectToAction(nameof(Perfil));
    }
}

[Authorize]
public class CompanyController(ApplicationDbContext db, UserManager<ApplicationUser> users, ImageUploadService uploads) : Controller
{
    [HttpGet("/empresa/registro/{userId}")]
    [AllowAnonymous]
    public IActionResult Register(string userId) => View(new EmpresaRegistroVm());

    [HttpPost("/empresa/registro/{userId}"), ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string userId, EmpresaRegistroVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var u = await users.FindByIdAsync(userId);
        if (u is null) return NotFound();
        string? logo = null;
        if (vm.Logo is { Length: > 0 })
        {
            var error = ImageUploadService.Validar(vm.Logo);
            if (error is not null) { ModelState.AddModelError(nameof(vm.Logo), error); return View(vm); }
            logo = await uploads.GuardarAsync(vm.Logo, "logos");
        }
        db.Empresas.Add(new Empresa
        {
            UsuarioId = u.Id, RazonSocial = vm.RazonSocial, Ruc = vm.Ruc,
            NombreContacto = vm.NombreContacto,
            Ciudad = vm.Ciudad, Telefono = vm.Telefono, Sector = vm.Sector, Descripcion = vm.Descripcion,
            Logo = logo
        });
        u.EstadoCuenta = EstadosCuenta.Pendiente;
        await db.SaveChangesAsync();
        TempData["Success"] = "Empresa registrada. Pendiente de aprobación.";
        return RedirectToAction("Login", "Account");
    }

    [HttpGet, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> Perfil()
    {
        var yo = (await users.GetUserAsync(User))!;
        ViewBag.Empresa = await db.Empresas.FirstOrDefaultAsync(e => e.UsuarioId == yo.Id);
        ViewBag.Documentos = await db.Documentos.Where(d => d.UsuarioId == yo.Id)
            .OrderByDescending(d => d.FechaSubida).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> SubirLogo(IFormFile? logo)
    {
        var error = ImageUploadService.Validar(logo);
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Perfil)); }
        var yo = (await users.GetUserAsync(User))!;
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.UsuarioId == yo.Id);
        if (empresa is null) { TempData["Error"] = "Aún no tiene perfil de empresa."; return RedirectToAction(nameof(Perfil)); }
        var anterior = empresa.Logo;
        empresa.Logo = await uploads.GuardarAsync(logo!, "logos");
        await db.SaveChangesAsync();
        uploads.Borrar(anterior);
        TempData["Success"] = "Logo actualizado.";
        return RedirectToAction(nameof(Perfil));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> GuardarDescripcion(string? descripcion)
    {
        var yo = (await users.GetUserAsync(User))!;
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.UsuarioId == yo.Id);
        if (empresa is null) { TempData["Error"] = "Aún no tiene perfil de empresa."; return RedirectToAction(nameof(Perfil)); }
        empresa.Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        await db.SaveChangesAsync();
        TempData["Success"] = "Descripción actualizada.";
        return RedirectToAction(nameof(Perfil));
    }

    // Editar datos adjuntos (todo menos la razón social, reservada al admin).
    [HttpGet, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> EditarDatos()
    {
        var yo = (await users.GetUserAsync(User))!;
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.UsuarioId == yo.Id);
        if (empresa is null) { TempData["Error"] = "Aún no tiene perfil de empresa."; return RedirectToAction(nameof(Perfil)); }
        return View(new EmpresaDatosVm
        {
            NombreContacto = empresa.NombreContacto, Ciudad = empresa.Ciudad,
            Telefono = empresa.Telefono, Sector = empresa.Sector,
            Descripcion = empresa.Descripcion
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> EditarDatos(EmpresaDatosVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var yo = (await users.GetUserAsync(User))!;
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.UsuarioId == yo.Id);
        if (empresa is null) { TempData["Error"] = "Aún no tiene perfil de empresa."; return RedirectToAction(nameof(Perfil)); }
        empresa.NombreContacto = vm.NombreContacto; empresa.Ciudad = vm.Ciudad;
        empresa.Telefono = vm.Telefono; empresa.Sector = vm.Sector;
        empresa.Descripcion = string.IsNullOrWhiteSpace(vm.Descripcion) ? null : vm.Descripcion.Trim();
        await db.SaveChangesAsync();
        TempData["Success"] = "Datos actualizados.";
        return RedirectToAction(nameof(Perfil));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Empresa)]
    public async Task<IActionResult> SubirDocumento(IFormFile? documento)
    {
        var error = ImageUploadService.ValidarDocumento(documento);
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Perfil)); }
        var yo = (await users.GetUserAsync(User))!;
        var archivo = await uploads.GuardarAsync(documento!, "acreditacion");
        db.Documentos.Add(new DocumentoAcreditacion
        { UsuarioId = yo.Id, Archivo = archivo, NombreOriginal = documento!.FileName });
        await db.SaveChangesAsync();
        TempData["Success"] = "Documento de acreditación subido.";
        return RedirectToAction(nameof(Perfil));
    }

    // Perfil público de cualquier empresa (avatar/nombre clicables).
    [HttpGet("/empresa/perfil/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Publico(string id)
    {
        var u = await db.Users.Include(x => x.Empresa).FirstOrDefaultAsync(x => x.Id == id && x.Rol == Roles.Empresa);
        if (u?.Empresa is null) return NotFound();
        ViewBag.Empresa = u.Empresa;
        ViewBag.Usuario = u;
        ViewBag.Ofertas = await db.Publicaciones
            .Where(p => p.AutorId == id && p.Activa)
            .OrderByDescending(p => p.FechaCreacion).Take(5).ToListAsync();
        return View();
    }
}
