FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/RunStats/RunStats.csproj src/RunStats/packages.lock.json src/RunStats/
# Not --locked-mode: the lock file holds an implicit package whose version follows the SDK, and the floating
# sdk:10.0 tag moves to a new SDK with every .NET patch. CI restores in locked mode with the SDK from global.json.
RUN dotnet restore src/RunStats/RunStats.csproj
COPY src/ src/
RUN dotnet publish src/RunStats/RunStats.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RunStats.dll"]
