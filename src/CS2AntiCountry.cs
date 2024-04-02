using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using static CounterStrikeSharp.API.Core.Listeners;
using CounterStrikeSharp.API;

namespace CS2CountryBlocker;

public sealed class PluginConfig : BasePluginConfig
{
	[JsonPropertyName("Countries")]
	public List<string> BlockedCountries { get; set; } = new List<string> { "DE", "FR", "HU" };

	[JsonPropertyName("SteamIDWhitelist")]
	public List<string> SteamIDWhitelist { get; set; } = new List<string> { "76561198345583467" };

	[JsonPropertyName("KickUnknwon")]
	public bool KickUnknownCountry { get; set; } = true;

	[JsonPropertyName("ConfigVersion")]
	public override int Version { get; set; } = 1;
}

[MinimumApiVersion(198)]
public class PluginCountryBlocker : BasePlugin, IPluginConfig<PluginConfig>
{
	public override string ModuleName => "CS2 Country Blocker";
	public override string ModuleAuthor => "K4ryuu";
	public override string ModuleDescription => "Block players from specific countries from joining the server.";
	public override string ModuleVersion => "1.0.0";

	public required PluginConfig Config { get; set; } = new PluginConfig();

	public void OnConfigParsed(PluginConfig config)
	{
		if (config.Version < Config.Version)
		{
			base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);
		}

		this.Config = config;
	}

	public override void Load(bool hotReload)
	{
		if (!File.Exists(Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb")))
		{
			Logger.LogError("GeoLite2-Country.mmdb not found in the plugin directory. You can download it from https://github.com/P3TERX/GeoLite.mmdb/releases");
			return;
		}

		RegisterListener<OnClientConnected>((slot) =>
		{
			CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

			if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV || player.IpAddress == null)
				return;

			string countryCode = GetPlayerCountryCode(player);

			if (Config.BlockedCountries.Contains(countryCode, StringComparer.OrdinalIgnoreCase) || (Config.KickUnknownCountry && countryCode == "??"))
			{
				if (Config.SteamIDWhitelist.Contains(player.SteamID.ToString()))
					return;

				Logger.LogInformation($"Blocked player {player.PlayerName} ({player.SteamID}) from joining the server. Country: {countryCode}");

				Server.NextFrame(() =>
				{
					Server.ExecuteCommand($"kickid {player.UserId} \"You are not allowed to join this server from this country.\"");
				});
			}
		});
	}

	public string GetPlayerCountryCode(CCSPlayerController player)
	{
		string? playerIp = player.IpAddress;

		if (playerIp == null)
			return "??";

		string[] parts = playerIp.Split(':');
		string realIP = parts.Length == 2 ? parts[0] : playerIp;

		using DatabaseReader reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb"));
		{
			try
			{
				MaxMind.GeoIP2.Responses.CountryResponse response = reader.Country(realIP);
				return response.Country.IsoCode ?? "??";
			}
			catch (AddressNotFoundException)
			{
				Logger.LogError($"The address {realIP} is not in the database.");
				return "??";
			}
			catch (GeoIP2Exception ex)
			{
				Logger.LogError($"Error: {ex.Message}");
				return "??";
			}
		}
	}
}