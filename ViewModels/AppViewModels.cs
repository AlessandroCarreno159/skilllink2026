using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.ViewModels;

public class RegisterVm
{
    [Required, RegularExpression(@"^\d{8}$", ErrorMessage = "El DNI debe tener 8 dígitos.")]
    public string Dni { get; set; } = string.Empty;

    [Required, EmailAddress] public string Email { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    [DataType(DataType.Password)] public string Password { get; set; } = string.Empty;

    [Required] public string Rol { get; set; } = "trabajador";
}

public class LoginVm
{
    [Required, RegularExpression(@"^\d{8}$", ErrorMessage = "Ingrese un DNI válido de 8 dígitos.")]
    public string Dni { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class TrabajadorRegistroVm
{
    [Required] public string NombreCompleto { get; set; } = string.Empty;
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Especialidad { get; set; }
    public string? Descripcion { get; set; }
    public string? Habilidades { get; set; }
    public string? Disponibilidad { get; set; }
    public IFormFile? Foto { get; set; }
    public IFormFile? DocumentoExperiencia { get; set; }
    public bool TieneAntecedentes { get; set; }
    [Required] public string DeclaracionAntecedentes { get; set; } = string.Empty;
}

public class EmpresaRegistroVm
{
    [Required] public string RazonSocial { get; set; } = string.Empty;
    public string? NombreContacto { get; set; }
    public string? Ruc { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Sector { get; set; }
    public string? Descripcion { get; set; }
    public IFormFile? Logo { get; set; }
}

public class PublicacionCreateVm
{
    [Required] public string Tipo { get; set; } = "oferta";
    public string? Titulo { get; set; }
    [Required] public string Contenido { get; set; } = string.Empty;
    public string? HabilidadesReq { get; set; }
    public string? Categoria { get; set; }
    public string? Modalidad { get; set; }
    public string? PrecioTexto { get; set; }
    public int? Vacantes { get; set; }
    public IFormFile? Imagen { get; set; }
}

public class SolicitudCreateVm
{
    public int PublicacionId { get; set; }
    public string Mensaje { get; set; } = "Postulación con un clic - perfil y experiencia adjuntos.";
}

// Edición de datos propios. El nombre (NombreCompleto/RazonSocial) NO está aquí:
// solo lo edita un administrador (AdminController.EditarNombre).
public class TrabajadorDatosVm
{
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Especialidad { get; set; }
    public string? Descripcion { get; set; }
    public string? Habilidades { get; set; }
    [Required] public string Disponibilidad { get; set; } = "disponible";
}

public class EmpresaDatosVm
{
    public string? NombreContacto { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Sector { get; set; }
    public string? Descripcion { get; set; }
}
