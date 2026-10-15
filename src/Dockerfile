# Web uygulamasının Docker imajı (src klasöründen derlenir: docker compose bu dosyayı kullanır).
# 1. aşama: .NET SDK ile derle ve yayınla. 2. aşama: yalnızca çalışma zamanını içeren küçük imaj.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Önce yalnızca proje dosyalarını kopyalayıp paketleri indir: kod değişince bu katman önbellekten gelir.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY ConferenceRoom.Domain/ConferenceRoom.Domain.csproj ConferenceRoom.Domain/
COPY ConferenceRoom.Infrastructure/ConferenceRoom.Infrastructure.csproj ConferenceRoom.Infrastructure/
COPY ConferenceRoom.Localization/ConferenceRoom.Localization.csproj ConferenceRoom.Localization/
COPY ConferenceRoom.Web/ConferenceRoom.Web.csproj ConferenceRoom.Web/
RUN dotnet restore ConferenceRoom.Web/ConferenceRoom.Web.csproj
COPY . .
RUN dotnet publish ConferenceRoom.Web/ConferenceRoom.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ConferenceRoom.Web.dll"]
