# syntax=docker/dockerfile:1.7
# Multi-stage build for the RentCollection.Web app.
# Build stage uses the SDK; runtime stage uses the smaller aspnet image.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first for a cached restore layer.
COPY RentCollection.Core/RentCollection.Core.csproj RentCollection.Core/
COPY RentCollection.Application/RentCollection.Application.csproj RentCollection.Application/
COPY RentCollection.Infrastructure/RentCollection.Infrastructure.csproj RentCollection.Infrastructure/
COPY RentCollection.Web/RentCollection.Web.csproj RentCollection.Web/
COPY RentCollection.Tests/RentCollection.Tests.csproj RentCollection.Tests/
RUN dotnet restore RentCollection.Web/RentCollection.Web.csproj

# Copy everything and publish.
COPY . .
RUN dotnet publish RentCollection.Web/RentCollection.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- Runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Run as non-root for defense-in-depth.
RUN groupadd --system app && useradd --system --gid app --uid 10001 app
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Health probe — readiness endpoint exercises the database connection.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "RentCollection.Web.dll"]
