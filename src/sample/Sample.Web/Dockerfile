# Sample web app image. Modeled on src/Tjb.Web/Dockerfile.
# Build context is the REPO ROOT (the reusable deploy workflow runs `docker build -f <path> .`).
# The sample is self-contained under src/sample, so only that tree is copied.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Token for restoring the framework packages (Tjb.Web.Framework / Tjb.Web.Hosting /
# Tjb.Web.Framework.Data) from GitHub Packages — src/sample/nuget.config expands
# %GH_PACKAGES_TOKEN%. Build-stage only; the runtime stage does not inherit it.
ARG GH_PACKAGES_TOKEN=""
ENV GH_PACKAGES_TOKEN=${GH_PACKAGES_TOKEN}

# Copy the self-contained sample solution (sln + nuget.config + all Sample.* projects).
COPY ["src/sample/", "src/sample/"]

WORKDIR /src/src/sample
RUN dotnet restore "Sample.sln"
RUN dotnet build "Sample.Web/Sample.Web.csproj" -c Release -o /app/build

FROM build AS publish
ARG VERSION=1.0.0
ARG ASSEMBLY_VERSION=1.0.0.0
ARG INFORMATIONAL_VERSION=1.0.0
RUN dotnet publish "Sample.Web/Sample.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:Version=${VERSION} /p:AssemblyVersion=${ASSEMBLY_VERSION} /p:InformationalVersion=${INFORMATIONAL_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "Sample.Web.dll"]
