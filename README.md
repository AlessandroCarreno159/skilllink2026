# SkillLink-dotnet

Migración fiel del proyecto Flask `skilllink` a **C# / .NET 10 / ASP.NET Core MVC / Razor / Identity / EF Core / MySQL**.

SkillLink es la plataforma peruana que conecta profesionales, técnicos, estudiantes y trabajadores con empresas y clientes.
Encuentra oportunidades. Conecta con talento.

## Asistente virtual (Gemini)

El chatbot usa **Gemini con *function calling*** para consultar publicaciones, técnicos, postulaciones, notificaciones y el FAQ de la plataforma. Por defecto se configura con el modelo gratuito `gemini-flash-lite-latest` y un respaldo `gemini-3.1-flash-lite`, adecuado para un piloto académico sin datos reales. Las respuestas se transmiten por el canal de Socket.io existente y se muestran en el widget flotante.

Configuración local:

```powershell
dotnet user-secrets set "Chatbot:ApiKey" "TU_API_KEY"
dotnet ef database update
dotnet run
```

Variables de entorno para producción: `Chatbot__ApiKey`, `Chatbot__Enabled`, `Chatbot__Model`, `Chatbot__FallbackModel`, `Chatbot__SupportUrl`. Si el modelo principal agota su cuota, el asistente reintenta con el modelo de respaldo. Redis y el gateway de Socket.io son opcionales para el funcionamiento HTTP; si no están disponibles, el chatbot usa memoria local y las respuestas llegan sin streaming en tiempo real.
