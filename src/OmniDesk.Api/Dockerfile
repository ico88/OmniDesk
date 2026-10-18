FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY src/OmniDesk.Api/OmniDesk.Api.csproj src/OmniDesk.Api/
RUN dotnet restore src/OmniDesk.Api/OmniDesk.Api.csproj
COPY src/OmniDesk.Api/ src/OmniDesk.Api/
RUN dotnet publish src/OmniDesk.Api/OmniDesk.Api.csproj -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "OmniDesk.Api.dll"]
