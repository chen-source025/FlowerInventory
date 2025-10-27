# 建置階段：使用 .NET SDK 8.0
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# 複製所有檔案並建置
COPY . ./
RUN dotnet publish -c Release -o out

# 執行階段：使用 ASP.NET Core Runtime 8.0
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

# 啟動應用程式
ENTRYPOINT ["dotnet", "FlowerInventory.dll"]
