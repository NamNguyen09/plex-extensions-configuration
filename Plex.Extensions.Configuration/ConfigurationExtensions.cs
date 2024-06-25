using System.Reflection;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Configuration.Json;

namespace Plex.Extensions.Configuration;
public static class ConfigurationExtensions
{
	public static async Task<WebApplicationBuilder> AddJsonConfigurationFilesAsync(this WebApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

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
				httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
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
