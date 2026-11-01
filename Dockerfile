FROM node:24-bookworm-slim AS frontend
WORKDIR /src
COPY package.json package-lock.json .npmrc ./
COPY ui/public/package.json ui/public/package.json
COPY ui/admin/package.json ui/admin/package.json
RUN --mount=type=secret,id=npm_token,env=NODE_AUTH_TOKEN npm ci
COPY tsconfig.json ./
COPY ui ui
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY NuGet.config ./
COPY apps/Manoir.HomeDocs.Api/Manoir.HomeDocs.Api.csproj apps/Manoir.HomeDocs.Api/
RUN dotnet restore apps/Manoir.HomeDocs.Api/Manoir.HomeDocs.Api.csproj
COPY apps/Manoir.HomeDocs.Api/ apps/Manoir.HomeDocs.Api/
RUN dotnet publish apps/Manoir.HomeDocs.Api/Manoir.HomeDocs.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown "$APP_UID" /data
WORKDIR /app
COPY --from=backend /app/publish/ ./
COPY --from=frontend /src/ui/public/dist/ ./wwwroot/public/
COPY --from=frontend /src/ui/admin/dist/ ./wwwroot/admin/
ENV ASPNETCORE_HTTP_PORTS=8080 HomeDocs__DataPath=/data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Manoir.HomeDocs.Api.dll"]
