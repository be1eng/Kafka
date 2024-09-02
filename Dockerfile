# Stage 1: Build the application using .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory
WORKDIR /app

# Copiar los archivos csproj y restaurar las dependencias
COPY *.csproj ./
RUN dotnet restore

# Copy the file from your host to your current location
COPY publish/ .
COPY appsettings.json .

# Exponer el puerto en el que la aplicación está escuchando
EXPOSE 5000

# Run the specified command within the container.
ENTRYPOINT ["dotnet", "HelloKafka.dll"]