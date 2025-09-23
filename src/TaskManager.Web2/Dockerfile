# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution file
COPY ["TaskManager.sln", "TaskManager.sln"]

# Copy all project directories
COPY ["src/", "src/"]

# Copy NuGet.config if it exists
COPY ["NuGet.config", "NuGet.config"]

# Set working directory to the web project
WORKDIR /src/src/TaskManager.Web2

# Restore dependencies for the entire solution
RUN dotnet restore "../../TaskManager.sln"

# Build the web project
RUN dotnet build "TaskManager.Web2.csproj" -c Release -o /app/build

# Publish the app
FROM build AS publish
ARG VERSION=1.0.0
ARG ASSEMBLY_VERSION=1.0.0.0
ARG INFORMATIONAL_VERSION=1.0.0
RUN dotnet publish "TaskManager.Web2.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:Version=${VERSION} /p:AssemblyVersion=${ASSEMBLY_VERSION} /p:InformationalVersion=${INFORMATIONAL_VERSION}

# Use the official ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "TaskManager.Web2.dll"]