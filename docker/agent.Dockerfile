FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /agent
COPY agent/ ./
RUN dotnet publish ProverbsAgent.csproj -c Release -o /agent/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runner

WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8000

COPY --from=build /agent/out ./

EXPOSE 8000

ENTRYPOINT ["./ProverbsAgent"]