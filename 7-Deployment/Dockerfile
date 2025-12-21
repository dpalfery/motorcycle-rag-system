FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["MotorcycleRAG.sln", "."]
COPY ["1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj", "1-Presentation/MotorcycleRAG.API/"]
COPY ["2-Application/MotorcycleRAG.Application/MotorcycleRAG.Application.csproj", "2-Application/MotorcycleRAG.Application/"]
COPY ["3-Domain/MotorcycleRAG.Domain/MotorcycleRAG.Domain.csproj", "3-Domain/MotorcycleRAG.Domain/"]
COPY ["3-Domain/MotorcycleRAG.Contracts/MotorcycleRAG.Contracts.csproj", "3-Domain/MotorcycleRAG.Contracts/"]
COPY ["4-Persistence/MotorcycleRAG.Persistence/MotorcycleRAG.Persistence.csproj", "4-Persistence/MotorcycleRAG.Persistence/"]

RUN dotnet restore "MotorcycleRAG.sln"
RUN dotnet publish "1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MotorcycleRAG.API.dll"]