namespace SkillLink_dotnet.Services;

/// <summary>
/// Guarda y resuelve imágenes subidas por usuarios (publicaciones, fotos de perfil).
/// Las rutas en BD son relativas a wwwroot/uploads, ej. "publicaciones/xxx.jpg".
/// Si el archivo no existe en disco se devuelve un placeholder para no romper las vistas.
/// </summary>
public sealed class ImageUploadService(IWebHostEnvironment env)
{
    public const long MaxBytes = 5 * 1024 * 1024; // 5 MB
    public static readonly string[] ExtensionesPermitidas = [".jpg", ".jpeg", ".png", ".webp"];
    public const string PlaceholderUrl = "/img/placeholder.svg";

    /// <summary>Valida extensión y tamaño. Retorna mensaje de error o null si es válido.</summary>
    public static string? Validar(IFormFile? file)
    {
        if (file is null || file.Length == 0) return "Seleccione un archivo.";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ExtensionesPermitidas.Contains(ext))
            return "Formato no permitido. Use JPG, PNG o WebP.";
        if (file.Length > MaxBytes)
            return "La imagen supera el máximo de 5 MB.";
        return null;
    }

    public static readonly string[] ExtensionesDocumento = [".pdf", ".jpg", ".jpeg", ".png", ".webp"];
    public const long MaxBytesDocumento = 10 * 1024 * 1024; // 10 MB

    /// <summary>Valida un documento de acreditación (PDF o imagen, hasta 10 MB).</summary>
    public static string? ValidarDocumento(IFormFile? file)
    {
        if (file is null || file.Length == 0) return "Seleccione un archivo.";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ExtensionesDocumento.Contains(ext))
            return "Formato no permitido. Use PDF, JPG, PNG o WebP.";
        if (file.Length > MaxBytesDocumento)
            return "El documento supera el máximo de 10 MB.";
        return null;
    }

    /// <summary>Guarda el archivo en wwwroot/uploads/{subcarpeta} con nombre GUID. Retorna ruta relativa para BD.</summary>
    public async Task<string> GuardarAsync(IFormFile file, string subcarpeta)
    {
        var dir = Path.Combine(env.WebRootPath, "uploads", subcarpeta);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using var fs = File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(fs);
        return $"{subcarpeta}/{name}";
    }

    /// <summary>URL pública de una ruta relativa de BD, o placeholder si es nula o el archivo falta en disco.</summary>
    public string Url(string? relativa)
    {
        if (!string.IsNullOrWhiteSpace(relativa))
        {
            var fisico = Path.Combine(env.WebRootPath, "uploads",
                relativa.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fisico)) return $"/uploads/{relativa}";
        }
        return PlaceholderUrl;
    }

    /// <summary>Borra el archivo físico de una ruta relativa de BD, si existe.</summary>
    public void Borrar(string? relativa)
    {
        if (string.IsNullOrWhiteSpace(relativa)) return;
        var fisico = Path.Combine(env.WebRootPath, "uploads",
            relativa.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fisico)) File.Delete(fisico);
    }

    /// <summary>Avatar del autor: foto de perfil (trabajador) o logo (empresa), con placeholder si falta.</summary>
    public string Avatar(Models.ApplicationUser? autor)
    {
        string? relativa = autor?.Rol switch
        {
            Models.Roles.Trabajador => autor?.Perfil?.Foto,
            Models.Roles.Empresa => autor?.Empresa?.Logo,
            _ => null
        };
        return Url(relativa);
    }

    /// <summary>Nombre público del autor para mostrar junto a sus contenidos.</summary>
    public static string NombrePublico(Models.ApplicationUser? autor)
    {
        if (autor?.Rol == Models.Roles.Trabajador
            && !string.IsNullOrWhiteSpace(autor.Perfil?.NombreCompleto))
            return autor.Perfil!.NombreCompleto;
        if (autor?.Rol == Models.Roles.Empresa
            && !string.IsNullOrWhiteSpace(autor.Empresa?.RazonSocial))
            return autor.Empresa!.RazonSocial;
        return autor?.Dni ?? autor?.Email ?? "Usuario";
    }

    /// <summary>Ruta al perfil público del usuario, o null si no tiene (admin).</summary>
    public static string? PerfilUrl(Models.ApplicationUser? autor)
        => autor?.Rol switch
        {
            Models.Roles.Trabajador => $"/trabajador/perfil/{autor.Id}",
            Models.Roles.Empresa => $"/empresa/perfil/{autor.Id}",
            _ => null
        };
}
