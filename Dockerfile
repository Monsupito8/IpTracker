FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["IpTracker.csproj", "./"]
RUN dotnet restore "IpTracker.csproj"
COPY . .
RUN dotnet build "IpTracker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IpTracker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IpTracker.dll"]