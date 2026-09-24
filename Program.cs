using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Render (y cualquier proxy TLS) termina HTTPS fuera del contenedor:
// se confía en X-Forwarded-Proto para no generar loops http→https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// MySQL + EF Core (Pomelo 9 + EF Core 9 sobre net10.0; ver README).
// NOTA compatibilidad: Pomelo.EntityFrameworkCore.MySql 10 aún estaba WIP,
// por eso se usa Pomelo 9.0.0 + EF Core 9.0.x, estable y compatible con .NET 10.
// Se usa versión fija (no AutoDetect) para que `dotnet ef` funcione sin MySQL en vivo.
// Ajuste la versión a la de su servidor (MySQL 8.0/8.4 o MariaDB) en appsettings "MySql:ServerVersion".
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
var serverVersionString = builder.Configuration["MySql:ServerVersion"] ?? "8.0.36";
var serverVersion = Microsoft.EntityFrameworkCore.ServerVersion.Parse(serverVersionString);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(conn, serverVersion, mySql =>
        mySql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddScoped<ReputacionService>();
builder.Services.AddScoped<NotificacionService>();
builder.Services.AddScoped<ImageUploadService>();
// Hasher que acepta los bcrypt del sistema Flask original y los migra a PBKDF2 al hacer login.
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, LegacyPasswordHasher>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseForwardedHeaders();

// Migraciones solo cuando se pide (primer deploy en Render con RUN_MIGRATIONS=true).
// La imagen runtime no trae `dotnet ef`, por eso se migran desde la app.
if (app.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    using var scope = app.Services.CreateScope();
    var dbm = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbm.Database.MigrateAsync();
    app.Logger.LogInformation("Migraciones aplicadas (RUN_MIGRATIONS=true).");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
// Sirve wwwroot, incluyendo archivos subidos en tiempo de ejecución (uploads/).
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Claims extra (Rol/EstadoCuenta) para el filtro CuentaActiva y el layout.
app.Use(async (ctx, next) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true && !ctx.User.HasClaim(c => c.Type == "EstadoCuenta"))
    {
        var um = ctx.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await um.GetUserAsync(ctx.User);
        if (u is not null)
        {
            var identity = (ClaimsIdentity)ctx.User.Identity!;
            identity.AddClaim(new Claim("EstadoCuenta", u.EstadoCuenta));
            identity.AddClaim(new Claim("Rol", u.Rol));
            identity.AddClaim(new Claim("Dni", u.Dni));
        }
    }
    await next();
});

try
{
    await IdentitySeed.EnsureAsync(app.Services, app.Configuration);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Seed inicial omitido: no se pudo conectar a MySQL. La app arranca igual.");
}
app.Run();
