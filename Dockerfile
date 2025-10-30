# 使用 .NET 8 SDK 進行建置
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 複製專案檔案並還原
COPY *.csproj .
RUN dotnet restore

# 複製原始碼並建置
COPY . .
RUN dotnet publish -c Release -o /app/publish

# 使用 ASP.NET 運行時
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# 設定環境變數
ENV ASPNETCORE_URLS=http://*:$PORT
ENV ASPNETCORE_ENVIRONMENT=Production

# 啟動應用程式
ENTRYPOINT ["dotnet", "FlowerInventory.dll"]