using System.Collections.Concurrent;
using AudioApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using Tomlyn.Extensions.Configuration;

namespace Sanky_Sounds;

[PluginMetadata(Id = "Sanky_Sounds", Version = "1.0.1", Name = "Sanky Sounds", Author = "T3Marius", Description = "No description.")]
public partial class Sanky_Sounds : BasePlugin
{
    private ServiceProvider? _provider;
    private static IAudioApi _audio = null!;
    private static PluginConfig Config { get; set; } = new();
    private ConcurrentDictionary<ulong, bool> _hasSoundEnabled { get; set; } = new();
    private DateTime _lastSoundTime = DateTime.MinValue;
    private readonly TimeSpan _globalCooldown = TimeSpan.FromSeconds(Config.GlobalCooldown);
    public Sanky_Sounds(ISwiftlyCore core) : base(core) { }
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        _audio = interfaceManager.GetSharedInterface<IAudioApi>("audio");
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration
            .InitializeTomlWithModel<PluginConfig>("config.toml", "SankySounds")
            .Configure(builder =>
            {
                builder.AddTomlFile("config.toml", optional: false, reloadOnChange: true);
            });

        ServiceCollection services = new();
        services
        .AddSwiftly(Core)
        .AddOptionsWithValidateOnStart<PluginConfig>
        ().BindConfiguration("SankySounds");

        _provider = services.BuildServiceProvider();
        Config = _provider.GetRequiredService<IOptions<PluginConfig>>().Value;

        foreach (var cmd in Config.ToggleSoundsCommands)
        {
            Core.Command.RegisterCommand(cmd, (context) =>
            {
                if (context.Sender is not IPlayer player)
                    return;

                bool currentlyEnabled = _hasSoundEnabled.TryGetValue(player.SteamID, out bool enabled) && enabled;

                bool newState = !currentlyEnabled;

                _hasSoundEnabled[player.SteamID] = newState;
                string state = newState ? $"enabled" : $"disabled";

                player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["prefix"] + Core.Translation.GetPlayerLocalizer(player)["sounds_toggle", state]);
            });
        }



    }
    [ClientChatHookHandler]
    public HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        IPlayer player = Core.PlayerManager.GetPlayer(playerId);
        if (HasSoundPermisson(player))
        {
            if (DateTime.UtcNow - _lastSoundTime < _globalCooldown)
            {
                double remaining = (_globalCooldown - (DateTime.UtcNow - _lastSoundTime)).TotalSeconds;

                player.SendMessage(
                    MessageType.Chat,
                    Core.Translation.GetPlayerLocalizer(player)["prefix"] +
                    Core.Translation.GetPlayerLocalizer(player)["sounds_cooldown", Math.Ceiling(remaining).ToString()]
                );

                return HookResult.Stop;
            }

            foreach (var kvp in Config.SankySounds)
            {
                string[] keys = kvp.Key.Split(',');
                string sound = kvp.Value;
                string prefix = Config.Prefix;

                foreach (string key in keys)
                {
                    string validKey = prefix + key;

                    if (text == validKey)
                    {
                        foreach (var otherPlayer in Core.PlayerManager.GetAllPlayers())
                        {
                            if (_hasSoundEnabled.TryGetValue(player.SteamID, out bool value) && value == true)
                            {
                                PlaySound(player, sound);
                            }
                        }
                        return Config.ShowMessages ? HookResult.Continue : HookResult.Stop;

                    }
                }
            }
        }

        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Post)]
    public HookResult EventPlayerConnectFull(EventPlayerConnectFull @event)
    {
        if (@event.UserIdPlayer is not IPlayer player)
            return HookResult.Continue;

        _hasSoundEnabled.TryAdd(player.SteamID, true);
        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Pre)]
    public HookResult EventPlayerDisconnect(EventPlayerDisconnect @event)
    {
        if (@event.UserIdPlayer is not IPlayer player)
            return HookResult.Continue;

        _hasSoundEnabled.TryRemove(player.SteamID, out _);
        return HookResult.Continue;
    }
    [GameEventHandler(HookMode.Pre)]
    public HookResult EventMapTransition(EventMapTransition @event)
    {
        _hasSoundEnabled.Clear();
        return HookResult.Continue;
    }
    private bool HasSoundPermisson(IPlayer player)
    {
        foreach (string permission in Config.Permissions)
        {
            if (ulong.TryParse(permission, out ulong steamId))
            {
                if (player.SteamID == steamId)
                    return true;
            }
            else
            {
                if (Core.Permission.PlayerHasPermission(player.SteamID, permission))
                    return true;
            }
        }

        return false;
    }
    private void PlaySound(IPlayer player, string sound)
    {
        IAudioChannelController controller = _audio.UseChannel("sanky_sounds");
        IAudioSource source = _audio.DecodeFromFile(Path.Combine(Core.PluginDataDirectory, sound));
        controller.SetSource(source);
        controller.SetVolume(player.PlayerID, 0.6f);
        controller.Play(player.PlayerID);
    }
    public override void Unload()
    {

    }
}
