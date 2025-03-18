using Newtonsoft.Json;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Loot Bouncer", "Sorrow/Arainrr", "0.5.0")]
    [Description("Empty the containers when players do not pick up all the items")]
    internal class LootBouncer : RustPlugin
    {
        [PluginReference] private Plugin Slap, Trade;

        private readonly Dictionary<uint, int> lootEntities = new Dictionary<uint, int>();

        private readonly List<string> barrels = new List<string>
        {
            "loot-barrel-1",
            "loot-barrel-2",
            "oil_barrel",
            "loot_barrel_1",
            "loot_barrel_2",
        };

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity?.net == null || player == null) return;
            if (Trade != null && Trade.Call<bool>("IsTradeBox", entity)) return;
            if (entity.net == null) return;
            var loot = entity.GetComponent<LootContainer>();
            if (loot?.inventory?.itemList == null || loot?.SpawnType == null) return;
            if (LootContainer.spawnType.AIRDROP.Equals(loot.SpawnType) && !configData.emptyAirdrop) return;
            if (LootContainer.spawnType.CRASHSITE.Equals(loot.SpawnType) && !configData.emptyCrashsite) return;
            if (loot is HackableLockedCrate && !configData.emptyHackableCrate) return;

            var entityId = entity.net.ID;
            if (!lootEntities.ContainsKey(entityId))
                lootEntities.Add(entityId, loot.inventory.itemList.Count);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity?.net == null || player == null) return;
            if (Trade != null && Trade.Call<bool>("IsTradeBox", entity)) return;
            var loot = entity.GetComponent<LootContainer>();
            if (loot?.inventory?.itemList == null || loot?.SpawnType == null) return;
            if (loot.inventory.itemList.Count == 0) return;
            var entityId = entity.net.ID;
            if (!lootEntities.ContainsKey(entityId)) return;
            if (loot.inventory.itemList.Count < lootEntities[entityId])
            {
                timer.Once(configData.timeBeforeLootDespawn, () =>
                {
                    if (loot?.inventory?.itemList == null) return;
                    DropUtil.DropItems(loot.inventory, loot.transform.position);
                    loot.Kill(BaseNetworkable.DestroyMode.Gib);
                    if (Slap != null && configData.slapPlayer)
                    {
                        if (player?.IPlayer == null) return;
                        Slap.Call("SlapPlayer", player.IPlayer);
                        Print(player, Lang("Container slap", player.UserIDString));
                    }
                });
            }
            lootEntities.Remove(entityId);
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || info?.HitEntity == null) return;
            if (attacker.IsNpc) return;
            if (barrels.Contains(info.HitEntity.ShortPrefabName))
            {
                var barrel = info.HitEntity as LootContainer;
                if (barrel == null) return;
                timer.Once(configData.timeBeforeLootDespawn, () =>
                {
                    if (barrel?.inventory?.itemList == null) return;
                    DropUtil.DropItems(barrel.inventory, barrel.transform.position);
                    barrel.Kill(BaseNetworkable.DestroyMode.Gib);
                    if (Slap != null && configData.slapPlayer)
                    {
                        if (attacker?.IPlayer == null) return;
                        Slap.Call("SlapPlayer", attacker.IPlayer);
                        Print(attacker, Lang("Barrel slap", attacker.UserIDString));
                    }
                });
            }
        }

        private void OnServerInitialized()
        {
            if (Slap == null && configData.slapPlayer)
                PrintWarning("Slap is not loaded, get it at https://umod.org/plugins/slap");
            if (!configData.emptyBarrel)
                Unsubscribe(nameof(OnPlayerAttack));
        }

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Time before the loot containers are empties")]
            public float timeBeforeLootDespawn = 30f;

            [JsonProperty(PropertyName = "Slaps players who don't empty containers")]
            public bool slapPlayer = false;

            [JsonProperty(PropertyName = "Empty the crates of the crashsites")]
            public bool emptyCrashsite = false;

            [JsonProperty(PropertyName = "Empty the hackable crates")]
            public bool emptyHackableCrate = false;

            [JsonProperty(PropertyName = "Empty the airdrops")]
            public bool emptyAirdrop = false;

            [JsonProperty(PropertyName = "Empty the barrel")]
            public bool emptyBarrel = false;

            [JsonProperty(PropertyName = "Chat prefix")]
            public string prefix = "[LootBouncer]:";

            [JsonProperty(PropertyName = "Chat prefix color")]
            public string prefixColor = "#00FFFF";

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong steamIDIcon = 0;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion ConfigurationFile

        #region LanguageFile

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Container slap"] = "You didn't empty the container. Slap you in the face",
                ["Barrel slap"] = "You didn't killed the barrel. Slap you in the face"
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void Print(BasePlayer player, string message) => Player.Message(player, message, $"<color={configData.prefixColor}>{configData.prefix}</color>", configData.steamIDIcon);

        #endregion LanguageFile
    }
}