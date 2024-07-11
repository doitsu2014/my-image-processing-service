FROM mcr.microsoft.com/dotnet/sdk:7.0-jammy-amd64 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0-jammy-amd64 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ImageProcessingService.Api/ImageProcessingService.Api.csproj", "ImageProcessingService.Api/"]
RUN dotnet restore "ImageProcessingService.Api/ImageProcessingService.Api.csproj"
COPY . .
WORKDIR "/src/ImageProcessingService.Api"
RUN dotnet build "ImageProcessingService.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ImageProcessingService.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ImageProcessingService.Api.dll"]
