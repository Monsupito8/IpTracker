# 1. Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["IpTracker.csproj", "./"]
RUN dotnet restore "IpTracker.csproj"

# Копируем весь код
COPY . .

# Собираем и публикуем
RUN dotnet publish "IpTracker.csproj" -c Release -o /app/publish

# 2. Этап рантайма
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Устанавливаем переменные окрурения для Linux
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# Копируем собранное приложение
COPY --from=build /app/publish .

# Открываем порт
EXPOSE 8080

# Запускаем приложение
ENTRYPOINT ["dotnet", "IpTracker.dll"]