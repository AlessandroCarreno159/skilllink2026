using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Perfil> Perfiles => Set<Perfil>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Antecedente> Antecedentes => Set<Antecedente>();
    public DbSet<ExperienciaLaboral> Experiencias => Set<ExperienciaLaboral>();
    public DbSet<Publicacion> Publicaciones => Set<Publicacion>();
    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();
    public DbSet<ComentarioValoracion> Valoraciones => Set<ComentarioValoracion>();
    public DbSet<ComentarioPublicacion> ComentariosHilo => Set<ComentarioPublicacion>();
    public DbSet<Denuncia> Denuncias => Set<Denuncia>();
    public DbSet<Reputacion> Reputaciones => Set<Reputacion>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<HistorialModeracion> HistorialModeracion => Set<HistorialModeracion>();
    public DbSet<BaneoDni> BaneosDni => Set<BaneoDni>();
    public DbSet<DocumentoAcreditacion> Documentos => Set<DocumentoAcreditacion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>(e =>
        {
            e.HasIndex(u => u.Dni).IsUnique();
            e.Property(u => u.Dni).HasMaxLength(8);
        });

        b.Entity<Perfil>(e =>
        {
            e.HasIndex(p => p.UsuarioId).IsUnique();
            e.HasOne(p => p.Usuario).WithOne(u => u.Perfil)
             .HasForeignKey<Perfil>(p => p.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Empresa>(e =>
        {
            e.HasIndex(x => x.UsuarioId).IsUnique();
            e.HasOne(x => x.Usuario).WithOne(u => u.Empresa)
             .HasForeignKey<Empresa>(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Antecedente>(e =>
        {
            e.HasIndex(a => a.UsuarioId).IsUnique();
        });

        b.Entity<Solicitud>(e =>
        {
            e.HasIndex(s => new { s.PublicacionId, s.TrabajadorId }).IsUnique();
            e.HasOne(s => s.Publicacion).WithMany(p => p.Solicitudes)
             .HasForeignKey(s => s.PublicacionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Estado).HasMaxLength(20);
        });

        b.Entity<Publicacion>(e =>
        {
            e.HasIndex(p => p.HabilidadesReq);
            e.Property(p => p.Tipo).HasMaxLength(20);
            e.Property(p => p.Categoria).HasMaxLength(30);
            e.Property(p => p.Modalidad).HasMaxLength(30);
            e.Property(p => p.PrecioTexto).HasMaxLength(60);
            e.HasIndex(p => p.Categoria);
            e.HasIndex(p => p.Modalidad);
        });

        b.Entity<ComentarioPublicacion>(e =>
        {
            e.HasIndex(c => new { c.PublicacionId, c.Activo });
        });

        b.Entity<Notificacion>(e =>
        {
            e.HasIndex(n => new { n.UsuarioId, n.Leida });
        });

        b.Entity<BaneoDni>(e =>
        {
            e.HasIndex(x => x.Dni).IsUnique();
        });

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
            {
                if (prop.GetMaxLength() is null && prop.Name is "Ciudad" or "Telefono" or "Imagen" or "Foto" or "Logo" or "Archivo" or "Evidencia" or "Enlace")
                    prop.SetMaxLength(512);
            }
        }
    }
}
