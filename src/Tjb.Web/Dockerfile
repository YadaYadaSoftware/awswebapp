# Use the official .NET 10 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Token for restoring Tjb.Web.Framework / Tjb.Web.Hosting from GitHub Packages.
# NuGet.config expands %GH_PACKAGES_TOKEN% in the github source credentials. This ENV lives only
# in the build stage; the final runtime stage (separate FROM) does not inherit it, so it is not
# baked into the pushed image.
ARG GH_PACKAGES_TOKEN=""
ENV GH_PACKAGES_TOKEN=${GH_PACKAGES_TOKEN}

# Copy solution file
COPY ["Tjb.sln", "Tjb.sln"]

# Copy all project directories
COPY ["src/", "src/"]

# Copy NuGet.config if it exists
COPY ["NuGet.config", "NuGet.config"]

# Set working directory to the web project
WORKDIR /src/src/Tjb.Web

# Restore dependencies for the entire solution
RUN dotnet restore "../../Tjb.sln"

# Build the web project
RUN dotnet build "Tjb.Web.csproj" -c Release -o /app/build

# Publish the app
FROM build AS publish
ARG VERSION=1.0.0
ARG ASSEMBLY_VERSION=1.0.0.0
ARG INFORMATIONAL_VERSION=1.0.0
RUN dotnet publish "Tjb.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:Version=${VERSION} /p:AssemblyVersion=${ASSEMBLY_VERSION} /p:InformationalVersion=${INFORMATIONAL_VERSION}

# Use the official ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "Tjb.Web.dll"]