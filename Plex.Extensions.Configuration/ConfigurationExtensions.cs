using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Configuration.Json;

namespace Plex.Extensions.Configuration;
public static class ConfigurationExtensions
{
    private static readonly ConcurrentDictionary<string, Lazy<string>> _keyValuePairs = new();
    public static async Task<WebApplicationBuilder> AddJsonConfigurationFilesAsync(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // remove default logging providers
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        if (string.IsNullOrEmpty(builder.Environment.EnvironmentName))
            builder.Environment.EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
        string environmentName = builder.Environment.EnvironmentName;
        var jsonBuilder = builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        if (!string.IsNullOrWhiteSpace(environmentName) && File.Exists("appsettings." + environmentName + ".json"))
        {
            jsonBuilder.AddJsonFile("appsettings." + environmentName + ".json", optional: true, reloadOnChange: true);
        }

        bool isLocal = Environment.GetEnvironmentVariable("IsLocal") == "true";
        if (!isLocal)
        {
            string? DAPR_SECRET_STORE = Environment.GetEnvironmentVariable("DAPR_SECRET_STORE");//"localsecretstore";
            string? keyVaultName = Environment.GetEnvironmentVariable("KV_NAME");
            if (string.IsNullOrWhiteSpace(DAPR_SECRET_STORE) && !string.IsNullOrWhiteSpace(keyVaultName))
            {
                builder.Configuration.AddAzureKeyVault(
                  new Uri($"https://{keyVaultName}.vault.azure.net/"),
                  new DefaultAzureCredential());
            }
            else if (!string.IsNullOrWhiteSpace(DAPR_SECRET_STORE))
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var baseURL = (Environment.GetEnvironmentVariable("AppSettings__BaseUrl") ?? "http://localhost") + ":" + (Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3500");

                //// Get secret from a local secret store
                ////var secret = await httpClient.GetStringAsync($"{baseURL}/v1.0/secrets/{DAPR_SECRET_STORE}/{SECRET_NAME}");
                var secrectBulk = await httpClient.GetStringAsync($"{baseURL}/v1.0/secrets/{DAPR_SECRET_STORE}/bulk");
                var secrectDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(secrectBulk) ?? [];
                Dictionary<string, string?> secrects = [];
                foreach (var item in secrectDict)
                {
                    if (item.Value == null || secrects.ContainsKey(item.Key)) continue;
                    secrects.Add(item.Key, item.Value[item.Key]);
                }
                IEnumerable<KeyValuePair<string, string?>> initialData = secrects;
                jsonBuilder.AddInMemoryCollection(initialData);
            }
        }

        jsonBuilder.AddEnvironmentVariables()
            .Build()
            .ExpandEnvironmentVariables(builder);

        return builder;
    }
    public static string GetConfigValue(this IConfiguration? configuration,
                                     string key,
                                     string defaultValue = "",
                                     string settingName = "AppSetting")
    {
        if (configuration == null) return defaultValue;

        string asSecretKey = $"{settingName}-{key}";
        if (settingName.Equals("AppSettings", StringComparison.InvariantCultureIgnoreCase))
        {
            asSecretKey = $"{settingName[..^1]}-{key}";
        }
        string evKey = $"{settingName}__{key}";
        string settingKey = $"{settingName}:{key}";
        string settingsKey = $"{settingName}s:{key}";

        if (_keyValuePairs.TryGetValue(asSecretKey, out var cached)) return cached.Value;
        if (_keyValuePairs.TryGetValue(evKey, out cached)) return cached.Value;
        if (_keyValuePairs.TryGetValue(settingKey, out cached)) return cached.Value;
        if (_keyValuePairs.TryGetValue(settingsKey, out cached)) return cached.Value;
        if (_keyValuePairs.TryGetValue(key, out cached)) return cached.Value;

        string? value = configuration[asSecretKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return _keyValuePairs.GetOrAdd(asSecretKey, _ => new Lazy<string>(() => value.ToExpandEnvironmentVariable())).Value;
        }

        value = Environment.GetEnvironmentVariable(evKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return _keyValuePairs.GetOrAdd(evKey, _ => new Lazy<string>(() => value.ToExpandEnvironmentVariable())).Value;
        }

        value = configuration.GetValue<string>(settingKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return _keyValuePairs.GetOrAdd(settingKey, _ => new Lazy<string>(() => value.ToExpandEnvironmentVariable())).Value;
        }

        value = configuration.GetValue<string>(settingsKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return _keyValuePairs.GetOrAdd(settingsKey, _ => new Lazy<string>(() => value.ToExpandEnvironmentVariable())).Value;
        }

        value = configuration.GetValue<string>(key) ?? defaultValue;
        return _keyValuePairs.GetOrAdd(key, _ => new Lazy<string>(() => value)).Value;
    }
    static string ToExpandEnvironmentVariable(this string? variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName)) return "";
        return Environment.ExpandEnvironmentVariables(variableName);
    }
    static IConfigurationRoot? ExpandEnvironmentVariables(this IConfigurationRoot configurtion, dynamic builder)
    {
        if (configurtion == null) return null;
        if (builder == null) return null;
        IEnumerable<IConfigurationProvider> providers = configurtion.Providers.Where(t => t.GetType() == typeof(JsonConfigurationProvider)).AsEnumerable();
        string? envVariablesConfig = builder.Configuration["ENV_VARIABLES"];
        string[]? envVariables = envVariablesConfig?.Split(",");
        foreach (var item in providers)
        {
            PropertyInfo? propInfo = typeof(JsonConfigurationProvider).GetProperty("Data", BindingFlags.NonPublic |
                                                                        BindingFlags.Instance | BindingFlags.GetField);
            if (propInfo == null) continue;
            object? settingVariables = propInfo.GetValue(item);
            if (settingVariables == null) continue;
            foreach ((string key, string value) in ((Dictionary<string, string>)settingVariables).Where(t => t.Key != null && t.Value != null))
            {
                if ((envVariables != null && !envVariables.Any(s => value.Contains(s)))
                    || !value.Contains('%')) continue;
                string settingValue = value;
                settingValue = Environment.ExpandEnvironmentVariables(settingValue);
                builder.Configuration[key] = settingValue;
            }
        }

        return configurtion;
    }
}
