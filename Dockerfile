# 建置階段：使用 .NET SDK 8.0
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# 複製專案檔並還復套件
COPY *.csproj ./
RUN dotnet restore

# 複製所有檔案並建置
COPY . ./
RUN dotnet publish -c Release -o out

# 執行階段：使用 ASP.NET Core Runtime 8.0
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

# 設定環境變數
ENV ASPNETCORE_URLS=http://*:$PORT
ENV ASPNETCORE_ENVIRONMENT=Production

# 暴露端口（可選，Render 會自動處理）
EXPOSE 8080

# 啟動應用程式
ENTRYPOINT ["dotnet", "FlowerInventory.dll"]