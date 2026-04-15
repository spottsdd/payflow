FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /build
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=builder /app .
EXPOSE 8083
ENV ASPNETCORE_URLS=http://+:8083
CMD ["dotnet", "PaymentProcessor.dll"]
