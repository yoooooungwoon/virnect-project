# VirnectMonitor 백엔드 컨테이너 이미지 (빌드 → 런타임 2단계)

# 1) 빌드 스테이지: SDK로 publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VirnectMonitor.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

# 2) 런타임 스테이지: ASP.NET Core 런타임만 (이미지 경량화)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# 컨테이너가 듣는 포트 (문서용 — 실제 바인딩은 compose의 ASPNETCORE_URLS)
EXPOSE 47892
ENTRYPOINT ["dotnet", "VirnectMonitor.dll"]
