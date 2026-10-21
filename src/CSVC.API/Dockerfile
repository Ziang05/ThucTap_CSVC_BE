FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/CSVC.Core/CSVC.Core.csproj src/CSVC.Core/
COPY src/CSVC.UseCases/CSVC.UseCases.csproj src/CSVC.UseCases/
COPY src/CSVC.Infrastructure/CSVC.Infrastructure.csproj src/CSVC.Infrastructure/
COPY src/CSVC.API/CSVC.API.csproj src/CSVC.API/
RUN dotnet restore src/CSVC.API/CSVC.API.csproj
COPY src/ src/
RUN dotnet publish src/CSVC.API/CSVC.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CSVC.API.dll"]
