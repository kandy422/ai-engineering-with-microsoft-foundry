FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NuGet.Config .
COPY lab/Capstone.csproj lab/
RUN dotnet restore lab/Capstone.csproj
COPY lab/ lab/
RUN dotnet publish lab/Capstone.csproj --no-restore -c Release -p:UseAppHost=false -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV Capstone__Mode=hosted ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Capstone.dll"]
