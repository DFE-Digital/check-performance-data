# Build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY DfE.Cypmd.HelloWorld.Web/*.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish DfE.Cypmd.HelloWorld.Web/DfE.Cypmd.HelloWorld.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "DfE.Cypmd.HelloWorld.Web.dll"]
