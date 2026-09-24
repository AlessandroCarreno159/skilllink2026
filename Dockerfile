# SkillLink-dotnet — imagen lista para Render (Docker).
# Incluye las imágenes semilla de wwwroot/uploads (viajan con el publish).
# Las subidas nuevas en producción son efímeras (disco de Render free).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Carpetas de subida escribibles por el usuario no-root `app`.
RUN mkdir -p wwwroot/uploads/publicaciones wwwroot/uploads/fotos wwwroot/uploads/logos \
    wwwroot/uploads/cv wwwroot/uploads/acreditacion wwwroot/uploads/evidencias \
    && chown -R app:app wwwroot/uploads
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# Render inyecta $PORT; por defecto 8080 para `docker run` local.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://*:${PORT:-8080} dotnet SkillLink-dotnet.dll"]
