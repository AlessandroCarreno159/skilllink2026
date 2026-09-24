using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.Models;

// 1-1 con ApplicationUser (lado trabajador). Equivale a tabla `perfiles`.
public class Perfil
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    public ApplicationUser? Usuario { get; set; }

    [Required] public string NombreCompleto { get; set; } = string.Empty;
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Especialidad { get; set; }
    public string? Descripcion { get; set; }
    public string? Habilidades { get; set; }
    public string Disponibilidad { get; set; } = "disponible";
    public string? Foto { get; set; }
    public string? CvPdf { get; set; }
}

// 1-1 con ApplicationUser (lado empresa). Equivale a tabla `empresas`.
public class Empresa
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    public ApplicationUser? Usuario { get; set; }

    [Required] public string RazonSocial { get; set; } = string.Empty;
    public string? Ruc { get; set; }
    public string? NombreContacto { get; set; } // "Nombre Completo" del mockup Datos de Empresa/Cliente
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Sector { get; set; }
    public string? Descripcion { get; set; }
    public string? Logo { get; set; }
}

// Equivale a tabla `antecedentes`.
public class Antecedente
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    public ApplicationUser? Usuario { get; set; }

    public bool TieneAntecedentes { get; set; }
    public string? TipoAntecedente { get; set; }
    public string? FechaAprox { get; set; }
    public string? SituacionActual { get; set; }
    [Required] public string Declaracion { get; set; } = string.Empty;
    public bool Revisado { get; set; }
    public bool ValidadoAdmin { get; set; }
}

// Equivale a tabla `experiencia_laboral` (1-N).
public class ExperienciaLaboral
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    public ApplicationUser? Usuario { get; set; }

    [Required] public string LugarTrabajo { get; set; } = string.Empty;
    [Required] public string CargoOficio { get; set; } = string.Empty;
    [Required] public string TiempoTrabajado { get; set; } = string.Empty;
    [Required] public string DescripcionActividades { get; set; } = string.Empty;
}
