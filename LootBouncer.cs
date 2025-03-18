using Newtonsoft.Json;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Loot Bouncer", "Sorrow/Arainrr", "0.4.0")]
    [Description("Empty the containers when players do not pick up all the items")]
    internal class LootBouncer : RustPlugin
    {
        [PluginReference]
        private Plugin Slap, Trade;

        private readonly Dictionary<uint, int> _lootEntity = new Dictionary<uint, int>();
        private readonly List<string> _barrels = new List<string>
        {
            "loot-barrel-1",
            "loot-barrel-2",
            "oil_barrel",
            "loot_barrel_1",
            "loot_barrel_2",
        };

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;
            if (Trade != null && Trade.Call<bool>("IsTradeBox", entity)) return;

            var entityId = entity.net.ID;
            var loot = entity.GetComponent<LootContainer>();
            if (loot == null || LootContainer.spawnType.AIRDROP.Equals(loot.SpawnType) && !_config._emptyAirdrop || LootContainer.spawnType.CRASHSITE.Equals(loot.SpawnType) && !_config._emptyCrashsite) return;
            if (loot is HackableLockedCrate && !_config._emptyHackableCrate) return;

            var originalValue = 0;
            if (!_lootEntity.TryGetValue(entityId, out originalValue))
            {
                _lootEntity.Add(entityId, loot.inventory.itemList.Count);
            }
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;
            if (Trade != null && Trade.Call<bool>("IsTradeBox", entity)) return;

            if (entity.net == null) return;
            var entityId = entity.net.ID;
            var loot = entity.GetComponent<LootContainer>();
            if (loot == null || LootContainer.spawnType.AIRDROP.Equals(loot.SpawnType) && !_config._emptyAirdrop || LootContainer.spawnType.CRASHSITE.Equals(loot.SpawnType) && !_config._emptyCrashsite) return;
            if (loot is HackableLockedCrate && !_config._emptyHackableCrate) return;

            var originalValue = 0;
            if (!_lootEntity.TryGetValue(entityId, out originalValue)) return;
            if (loot.inventory.itemList.Count < originalValue)
            {
                if (loot.inventory.itemList.Count == 0) return;
                timer.Once(_config._timeBeforeLootDespawn, () =>
                {
                    if (loot?.inventory == null) return;
                    DropUtil.DropItems(loot?.inventory, loot.transform.position);
                    loot.Kill(BaseNetworkable.DestroyMode.Gib);
					if (Slap != null && _config._slapPlayer)
					{
						Slap.Call("SlapPlayer", player.IPlayer);
						Player.Message(player, lang.GetMessage("Container slap", this, player.UserIDString), $"<color={_config.PrefixColor}>{_config.Prefix}</color>", _config.SteamIDIcon);
					}
                });
            }
            _lootEntity.Remove(entityId);
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || info?.HitEntity == null) return;
            if (attacker.IsNpc) return;
            if (_barrels.Contains(info.HitEntity.ShortPrefabName))
            {
                if (info.HitEntity != null && !info.HitEntity.IsDestroyed)
                {
                    LootContainer barrel = info.HitEntity as LootContainer;
                    if (barrel == null) return;
                    timer.Once(_config._timeBeforeLootDespawn, () =>
                    {
                        if (barrel == null) return;
                        DropUtil.DropItems(barrel.inventory, barrel.transform.position);
                        barrel.Kill(BaseNetworkable.DestroyMode.Gib);
						if (Slap != null && _config._slapPlayer)
						{
							Slap.Call("SlapPlayer", attacker.IPlayer);
							Player.Message(attacker, lang.GetMessage("Barrel slap", this, attacker.UserIDString), $"<color={_config.PrefixColor}>{_config.Prefix}</color>", _config.SteamIDIcon);
						}
                    });
                }
            }
        }

        private void OnServerInitialized()
        {
            if (Slap == null && _config._slapPlayer)
            {
                PrintWarning("Slap is not loaded, get it at https://umod.org");
            }
            if (!_config._emptyBarrel)
            {
                Unsubscribe(nameof(OnPlayerAttack));
            }
        }

        #region Configuration

        private ConfigFile _config;

        private class ConfigFile
        {
            [JsonProperty(PropertyName = "Time before the loot containers are empties")]
            public float _timeBeforeLootDespawn { get; set; } = 30f;

            [JsonProperty(PropertyName = "Empty the airdrops")]
            public bool _emptyAirdrop { get; set; } = false;

            [JsonProperty(PropertyName = "Empty the crates of the crashsites")]
            public bool _emptyCrashsite { get; set; } = false;

            [JsonProperty(PropertyName = "Slaps players who don't empty containers")]
            public bool _slapPlayer { get; set; } = false;

            [JsonProperty(PropertyName = "Empty the hackable crates")]
            public bool _emptyHackableCrate { get; set; } = false;

            [JsonProperty(PropertyName = "Empty the barrel")]
            public bool _emptyBarrel { get; set; } = false;
            [JsonProperty(PropertyName = "Prefix")]
            public string Prefix { get; set; } = "[LootBouncer]:";

            [JsonProperty(PropertyName = "Prefix color")]
            public string PrefixColor { get; set; } = "#00FFFF";

            [JsonProperty(PropertyName = "SteamID icon")]
            public ulong SteamIDIcon { get; set; } = 0;


            public static ConfigFile DefaultConfig()
            {
                return new ConfigFile();
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigFile>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("Config has corrupted or incorrectly formatted");
                return;
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = ConfigFile.DefaultConfig();
            PrintWarning("Creating a new configuration file");
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        #endregion Configuration
        #region Language

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Container slap"] = "You didn't empty the container. Slap you in the face",
                ["Barrel slap"] = "You didn't killed the barrel. Slap you in the face"
            }, this);
        }

        #endregion Language
    }
}