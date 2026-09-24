# SkillLink-dotnet

Migración fiel del proyecto Flask `skilllink` a **C# / .NET 10 / ASP.NET Core MVC / Razor / Identity / EF Core / MySQL**.

## Decisión de compatibilidad (documentada)
- Target: `net10.0` (SDK 10.0.302).
- `Pomelo.EntityFrameworkCore.MySql 9.0.0` + `Microsoft.EntityFrameworkCore* 9.0.9` + `Identity.EntityFrameworkCore 9.0.9`.
- Motivo: Pomelo EF Core 10 aún estaba WIP (PR #2019); Pomelo 9 + EF 9 es estable y compatible con .NET 10 según el mantenedor. Subir a Pomelo 10 cuando sea estable no requiere cambios de código.

## Configurar MySQL (manual)
1. Crear BD vacía: `CREATE DATABASE skilllink_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;`
2. Editar `appsettings.json` → `ConnectionStrings:DefaultConnection` con `server`, `user`, `password` reales.
3. Opcional admin inicial: `AdminSeed:Password` (nunca commitear el real).

## Comandos
```powershell
dotnet restore
dotnet tool install --global dotnet-ef   # solo una vez
dotnet ef migrations add InitialCreate -o Data/Migrations
dotnet ef database update
dotnet run
```

## Base de datos (MySQL, puerto desde SQLite 18/09/2026)
- La app usa solo MySQL: la conexión sale de `user-secrets`
  (`server=localhost`, `database=skilllink_db`). El paquete
  `Microsoft.EntityFrameworkCore.Sqlite` y el switch `UseSqlite` se eliminaron
  (el aviso NU1903 de `SQLitePCLRaw` desaparece con ello).
  `skilllink_local.db` queda intacto como respaldo/rollback, ya no se usa.
- Migraciones: `Data/Migrations/BaselineMySql` (única, generada contra MySQL/Pomelo 9:
  `longtext`, `datetime(6)`, `tinyint(1)`, `utf8mb4`). Las migraciones sabor-SQLite
  quedaron como referencia en `Backups/Migrations_sqlite_legacy/*.cs.bak` (no compilan).
- Puerto de datos: ETL `skilllink_local.db` → `skilllink_db` (142 filas, conteos 1:1,
  IDs conservados, `AUTO_INCREMENT = MAX(id)+1`, `FOREIGN_KEY_CHECKS=0` durante la carga).
  Respaldos en `Backups/`: `skilllink_local_20260918_previo_mysql.db`,
  `mysql_skilllink_db_20260918_previo.sql` (MySQL anterior, 104 filas),
  `mysql_skilllink_db_20260916.sql` y `skilllink_backup_20260915_seguridad.db` (anteriores).
- Imágenes: sin columnas BLOB en ningún modelo; todo son rutas relativas
  (`fotos/`, `logos/`, `publicaciones/`, …) servidas desde `wwwroot/uploads/`
  por `ImageUploadService` + `UseStaticFiles`. El puerto solo copió strings,
  los archivos en disco siguen válidos y las subidas nuevas caen en la misma carpeta.

## Notas
- Login por **DNI (UserName de Identity)**, fiel al Flask. `UserName = Dni`.
- Solicitudes = `postulaciones`: postular / mis / recibidas / aceptar-rechazar (+notificación) / eliminar (soft).
- El proyecto Flask original no fue modificado; todo vive en `SkillLink-dotnet/`.

## Despliegue (GitHub + Docker en Render + MySQL externo)
1. **MySQL en la nube** (Render no ofrece MySQL): crear servicio en Clever Cloud/Aiven/propio,
   BD `utf8mb4`, y anotar la cadena `server=...;port=3306;database=...;user=...;password=...`
   más la versión real del servidor (ej. `8.0.36`).
2. **GitHub** (repo privado recomendado). Las semillas de `wwwroot/uploads` ya están
   trackeadas; los archivos nuevos de usuarios se ignoran solos. Secuencia:
   ```powershell
   git init; git branch -M main
   git add -f wwwroot/uploads   # semillas (el .gitignore cubre solo lo nuevo)
   git add .; git status         # verificar que NO entren .db/.sql/Backups/secrets
   git commit -m "SkillLink desplegable en Render"
   git remote add origin https://github.com/TU_USUARIO/TU_REPO.git
   git push -u origin main
   ```
   ⚠️ Jamás subir `session-*.md`, `Lista1OpenCode.md`, `Backups/`, `*.db`, `*.sql` ni secretos.
3. **Render**: New → Web Service → seleccionar el repo (detecta `Dockerfile`) o Blueprint con
   `render.yaml`. Cargar env vars: `ConnectionStrings__DefaultConnection`,
   `MySql__ServerVersion`, `AdminSeed__Password` (temporal), `RUN_MIGRATIONS=true`.
4. Verificar `/Feed`, entrar como admin (DNI `10000001`), y luego poner
   `RUN_MIGRATIONS=false` y rotar/quitar `AdminSeed__Password`.
5. **Datos actuales (opcional)**: portar las filas del MySQL local al MySQL nube con el mismo
   ETL (`FOREIGN_KEY_CHECKS=0`, IDs conservados) para que las rutas coincidan con las
   imágenes semilla de la imagen Docker.
6. **Límites**: disco efímero (lo subido en producción se pierde al redeploy; las semillas no,
   viajan en la imagen), cold starts en plan free, y DataProtection en memoria (las sesiones
   se invalidan al reiniciar).
