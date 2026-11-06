# SkillLink-dotnet

Migración fiel del proyecto Flask `skilllink` a **C# / .NET 10 / ASP.NET Core MVC / Razor / Identity / EF Core / MySQL**.

SkillLink es la plataforma peruana que conecta profesionales, técnicos, estudiantes y trabajadores con empresas y clientes.
Encuentra oportunidades. Conecta con talento.

## Configuración de Algolia

La integración usa estos valores separados:

| Configuración | Variable de entorno | Uso | Secreto |
|---|---|---|---|
| `Algolia:ApplicationId` | `Algolia__ApplicationId` | Identificador de la aplicación | No |
| `Algolia:IndexName` | `Algolia__IndexName` | Índice (`skilllink_index`) | No |
| `Algolia:IndexingApiKey` | `Algolia__IndexingApiKey` | Indexar, borrar y aplicar settings | **Sí, solo servidor** |
| `Algolia:SearchApiKey` | `Algolia__SearchApiKey` | Búsqueda del navegador | Pública, pero restringida |

> Seguridad: la clave privilegiada que estuvo en `appsettings.json` debe rotarse en Algolia antes de desplegar. No reutilice ese valor.

`appsettings.json` ya no contiene claves. En desarrollo use User Secrets:

```powershell
dotnet user-secrets set "Algolia:IndexingApiKey" "<clave-de-indexacion>"
dotnet user-secrets set "Algolia:SearchApiKey" "<search-key-restringida>"
```

En Render configure `Algolia__IndexingApiKey` y `Algolia__SearchApiKey` como variables con `sync: false` (ver `render.yaml`). No las escriba en `appsettings.json`, `appsettings.Development.json`, Dockerfile ni JavaScript. Si el servicio aún tiene `Algolia__ApiKey`, elimínela después de migrar al nuevo nombre (existe un alias temporal en el código).

### Search API Key restringida (configuración manual en Algolia)

Cree una Search API Key nueva en el panel de Algolia (o actualice la existente) con:

- ACL: **solo `search`** (sin `browse`, `settings` ni `listIndexes`).
- `restrictIndices`: `["skilllink_index"]`.
- `maxQueriesPerIPPerHour`: por ejemplo `1000` (ajuste según tráfico).
- `maxHitsPerQuery`: por ejemplo `20`.
- `restrictSources`: opcional; si el tráfico sale siempre de un rango conocido, restrinja a ese rango. Si no lo conoce, déjelo vacío para no romper el buscador.
- `validity`: opcional; si define caducidad, recuerde rotarla.

El frontend solo recibe `Algolia__SearchApiKey`. La clave de indexación nunca se renderiza en vistas ni se envía al navegador.

### Mantenimiento del índice

```powershell
# Reindexado completo: vacía skilllink_index y vuelve a indexar solo publicaciones activas.
dotnet run -- --reindex

# El seed de 150 publicaciones reindexa automáticamente las activas al terminar.
dotnet run -- --seed-150

# Seed demo (desarrollo); también reconstruye el índice con las activas.
dotnet run -- --seed-demo
```

Las publicaciones que pasan a `Activa = false` se eliminan del índice; al reactivarlas desde el panel de administración se vuelven a indexar.

## Compilación

```powershell
dotnet build
```
