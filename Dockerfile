FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
VOLUME ["/app/resources"]
COPY out/ ./
ENTRYPOINT ["dotnet", "KosmoHub.dll", "--urls", "http://0.0.0.0:80"]
