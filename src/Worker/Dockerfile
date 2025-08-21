# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ./Shared ./Shared
COPY ./Worker ./Worker
RUN dotnet restore Worker/Worker.csproj
RUN dotnet publish Worker/Worker.csproj -c Release -o /app

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Worker.dll"]
