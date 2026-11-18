# ---------- 1. Build Web UI ----------
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
# Xuất ra /web/dist thay vì ../src/... (không có trong stage này)
RUN npx tsc -b && npx vite build --outDir dist --emptyOutDir

# ---------- 2. Build API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NetTranslate.slnx Directory.Build.props ./
COPY src/NetTranslate.Domain/NetTranslate.Domain.csproj src/NetTranslate.Domain/
COPY src/NetTranslate.Application/NetTranslate.Application.csproj src/NetTranslate.Application/
COPY src/NetTranslate.Infrastructure/NetTranslate.Infrastructure.csproj src/NetTranslate.Infrastructure/
COPY src/NetTranslate.Api/NetTranslate.Api.csproj src/NetTranslate.Api/
RUN dotnet restore src/NetTranslate.Api/NetTranslate.Api.csproj
COPY src/ src/
RUN dotnet publish src/NetTranslate.Api/NetTranslate.Api.csproj -c Release -o /app --no-restore

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot

ENV ASPNETCORE_URLS=http://+:5000 \
    Models__Path=/app/models \
    ConnectionStrings__Default="Data Source=/app/data/nettranslate.db" \
    Storage__FilesPath=/app/data/files

VOLUME ["/app/models", "/app/data"]
EXPOSE 5000
ENTRYPOINT ["dotnet", "NetTranslate.Api.dll"]
