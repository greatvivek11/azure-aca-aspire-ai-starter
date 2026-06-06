# Use the official .NET 10 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project files
COPY . .

# Build the application without Native AOT for container compatibility.
RUN dotnet restore
RUN dotnet build Worker.csproj -c Release -p:PublishAot=false -o ./buildout

# Use the ASP.NET Core runtime image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /src/buildout .
EXPOSE 8081
ENTRYPOINT ["dotnet", "Worker.dll"]