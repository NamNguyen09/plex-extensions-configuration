# Plex.Extensions.Configuration

A .NET 8 library that simplifies ASP.NET Core application configuration setup. Provides a single-call builder extension that loads JSON config files, integrates **Azure Key Vault** or **Dapr secret store**, expands environment variables in config values, and a multi-format configuration value resolver with caching.

## Features

- **One-call startup configuration** — loads `appsettings.json` + environment-specific overrides, clears default loggers, removes the Kestrel `Server` header
- **Azure Key Vault integration** — automatically adds Key Vault as a configuration source when `KV_NAME` is set
- **Dapr secret store integration** — bulk-fetches secrets from the Dapr sidecar when `DAPR_SECRET_STORE` is set
- **Environment variable expansion** — resolves `%VAR%` placeholders in JSON config values at startup
- **Multi-format config resolver** — searches secret-key, environment variable, colon-delimited, and raw key formats with in-memory caching

## Installation

```bash
dotnet add package Plex.Extensions.Configuration
```

## Usage

### Application startup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Loads JSON configs, integrates secrets (Key Vault or Dapr), expands env vars
await builder.AddJsonConfigurationFilesAsync();

var app = builder.Build();
```

This single call handles:

1. Clears default logging providers
2. Removes the Kestrel `Server` response header (security hardening)
3. Loads `appsettings.json` (with reload-on-change)
4. Loads `appsettings.{Environment}.json` if it exists
5. Integrates secrets from Azure Key Vault or Dapr (non-local environments only)
6. Adds environment variables as a configuration source
7. Expands `%VAR%` tokens in all JSON config values

### Reading configuration values

```csharp
// Basic usage — searches multiple key formats automatically
string value = configuration.GetConfigValue("MyKey");

// With a default fallback
string value = configuration.GetConfigValue("MyKey", defaultValue: "fallback");

// With a custom section name (default is "AppSetting")
string value = configuration.GetConfigValue("MyKey", settingName: "CustomSection");
```

`GetConfigValue` searches for the key in this priority order:

1. In-memory cache (static `ConcurrentDictionary`)
2. Secret key format — `AppSetting-MyKey`
3. Environment variable — `AppSetting__MyKey`
4. Colon-delimited — `AppSetting:MyKey`
5. Pluralized section — `AppSettings:MyKey`
6. Raw key — `MyKey`

Resolved values are cached for subsequent lookups and have `%VAR%` tokens expanded automatically.

## Secret Store Configuration

### Azure Key Vault

Set the `KV_NAME` environment variable to your Key Vault name. The library uses `DefaultAzureCredential` for authentication.

```env
KV_NAME=my-keyvault-name
```

### Dapr Secret Store

Set the `DAPR_SECRET_STORE` environment variable to the Dapr secret store component name. Secrets are bulk-fetched from the Dapr sidecar at startup.

```env
DAPR_SECRET_STORE=my-secret-store
```

> Key Vault and Dapr are mutually exclusive — if `DAPR_SECRET_STORE` is set, it takes precedence.

### Local Development

Set `IsLocal=true` to skip secret store integration entirely (uses only JSON files and environment variables).

## Environment Variable Expansion

Config values in `appsettings.json` can contain `%VAR%` placeholders:

```json
{
  "ConnectionStrings": {
    "Default": "Server=%DB_HOST%;Database=%DB_NAME%;User=%DB_USER%;"
  }
}
```

These are expanded at startup using `Environment.ExpandEnvironmentVariables()`. Optionally, set `ENV_VARIABLES` to a comma-separated list to restrict which variables are expanded.

## API Reference

| Method | Target | Description |
|--------|--------|-------------|
| `AddJsonConfigurationFilesAsync()` | `WebApplicationBuilder` | Full startup configuration setup |
| `GetConfigValue(key, defaultValue?, settingName?)` | `IConfiguration` | Multi-format config key resolver with caching |

## Dependencies

| Package | Version |
|---------|---------|
| Azure.Extensions.AspNetCore.Configuration.Secrets | 1.5.x |
| Azure.Identity | 1.21.x |

## License

[Plex-Solution Community Source-Available License](LICENSE.md) — free for non-commercial use only.
