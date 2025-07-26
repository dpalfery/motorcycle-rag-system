FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MotorcycleRAG.sln", "."]
COPY ["src/", "src/"]
RUN dotnet restore "MotorcycleRAG.sln"
RUN dotnet publish "src/MotorcycleRAG.API/MotorcycleRAG.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MotorcycleRAG.API.dll"]