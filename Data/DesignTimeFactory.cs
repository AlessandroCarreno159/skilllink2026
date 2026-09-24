using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SkillLink_dotnet.Data;

/// <summary>
/// Factory de tiempo de diseño para que `dotnet ef migrations` no necesite
/// un MySQL en vivo para detectar la versión (evita ServerVersion.AutoDetect).
/// Lee la misma conexión que la app (appsettings + user-secrets + entorno).
/// Solo MySQL (el modo SQLite se eliminó el 18/09/2026).
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<ApplicationDbContext>()
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        var conn = config.GetConnectionString("DefaultConnection")
            ?? "server=localhost;port=3306;database=skilllink_db;user=root;password=root";
        var version = Microsoft.EntityFrameworkCore.ServerVersion.Parse(
            config["MySql:ServerVersion"] ?? "26.7.0");
        options.UseMySql(conn, version);
        return new ApplicationDbContext(options.Options);
    }
}
