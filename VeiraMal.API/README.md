# VeiraMal API

This is the .NET 8 Web API backend for the VeiraMal HR Analysis Portal.

## Features

- JWT Authentication for SuperAdmins  
- Secure Excel Upload via EPPlus  
- SQL Server backend with Entity Framework Core  
- Designed for Azure deployment  

## Getting Started

1. Clone the repository
2. Create your own `appsettings.json` file locally (this file is ignored in Git)

### 🔐 Sample `appsettings.json` template:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your_server;Initial Catalog=your_db;User ID=your_user;Password=your_password;"
  },
  "Jwt": {
    "Key": "Your_Development_JWT_Key",
    "Issuer": "VeiraMalAPI",
    "Audience": "VeiraMalUsers"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}

3. Run with:

```bash
dotnet run
