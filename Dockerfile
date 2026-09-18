FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NuGet.Config Frpm.slnx ./
COPY src ./src
RUN dotnet restore src/Frpm/Frpm.csproj
RUN dotnet publish src/Frpm/Frpm.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown -R app:app /data /app
USER app
COPY --from=build --chown=app:app /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__DefaultConnection="Data Source=/data/frpm.db" \
    Frpm__Storage__DataDirectory=/data
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl --fail http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Frpm.dll"]
