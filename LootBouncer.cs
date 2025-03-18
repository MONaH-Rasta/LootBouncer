using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Loot Bouncer", "Sorrow/Arainrr", "1.0.4")]
    [Description("Empty the containers when players do not pick up all the items")]
    internal class LootBouncer : RustPlugin
    {
        [PluginReference] private readonly Plugin Slap, Trade;
        private readonly Dictionary<uint, int> lootEntities = new Dictionary<uint, int>();
        private readonly Dictionary<uint, HashSet<ulong>> entityPlayers = new Dictionary<uint, HashSet<ulong>>();
        private readonly Dictionary<uint, Timer> lootDestroyTimer = new Dictionary<uint, Timer>();

        private void OnServerInitialized()
        {
            UpdateSettings();
            if (configData.slapPlayer && Slap == null)
                PrintError("Slap is not loaded, get it at https://umod.org/plugins/slap");
        }

        private void UpdateSettings()
        {
            foreach (var entityPrefab in GameManifest.Current.entities)
            {
                var lootContainer = GameManager.server.FindPrefab(entityPrefab.ToLower())?.GetComponent<LootContainer>();
                if (lootContainer != null && !string.IsNullOrEmpty(lootContainer.ShortPrefabName) && !configData.lootContainerSettings.ContainsKey(lootContainer.ShortPrefabName))
                    configData.lootContainerSettings.Add(lootContainer.ShortPrefabName, lootContainer.ShortPrefabName.Contains("stocking") ? false : true);
            }
            SaveConfig();
        }

        private void Unload()
        {
            foreach (var timer in lootDestroyTimer.Values)
                timer?.Destroy();
        }

        private void OnLootEntity(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null || player == null) return;
            var obj = Trade?.Call("IsTradeBox", lootContainer);
            if (obj != null && obj is bool && (bool)obj) return;
            if (configData.lootContainerSettings.ContainsKey(lootContainer.ShortPrefabName) && !configData.lootContainerSettings[lootContainer.ShortPrefabName]) return;

            var entityID = lootContainer.net.ID;
            if (!lootEntities.ContainsKey(entityID)) lootEntities.Add(entityID, lootContainer.inventory.itemList.Count);
            if (!entityPlayers.ContainsKey(entityID)) entityPlayers.Add(entityID, new HashSet<ulong> { player.userID });
            else entityPlayers[entityID].Add(player.userID);
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null || player == null) return;
            var entityID = lootContainer.net.ID;
            if (lootContainer.inventory == null || lootContainer.inventory.itemList == null || lootContainer.inventory.itemList.Count <= 0)
            {
                lootEntities.Remove(entityID);
                if (entityPlayers.ContainsKey(entityID)) entityPlayers[entityID].Remove(player.userID);
                return;
            }
            if (lootEntities.ContainsKey(entityID) && entityPlayers.ContainsKey(entityID))
            {
                if (lootContainer.inventory.itemList.Count < lootEntities[entityID])
                {
                    if (!lootDestroyTimer.ContainsKey(entityID))
                    {
                        lootDestroyTimer.Add(entityID, timer.Once(configData.timeBeforeLootEmpty, () =>
                        {
                            if (lootContainer == null || lootContainer.IsDestroyed) return;
                            lootContainer.DropItems();
                            lootContainer.RemoveMe();
                        }));
                    }
                }
                else entityPlayers[entityID].Remove(player.userID);
                lootEntities.Remove(entityID);
                EmptyJunkPile(lootContainer);
            }
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || info?.HitEntity == null) return;
            if (attacker.IsNpc || !attacker.userID.IsSteamId()) return;
            if (!info.HitEntity.ShortPrefabName.Contains("barrel")) return;
            if (configData.lootContainerSettings.ContainsKey(info.HitEntity.ShortPrefabName) && !configData.lootContainerSettings[info.HitEntity.ShortPrefabName]) return;
            var barrel = info.HitEntity as LootContainer;
            if (barrel == null || barrel.net == null) return;

            var barrelID = barrel.net.ID;
            if (!entityPlayers.ContainsKey(barrelID)) entityPlayers.Add(barrelID, new HashSet<ulong> { attacker.userID });
            else entityPlayers[barrelID].Add(attacker.userID);

            if (!lootDestroyTimer.ContainsKey(barrelID))
            {
                lootDestroyTimer.Add(barrelID, timer.Once(configData.timeBeforeLootEmpty, () =>
                {
                    if (barrel == null || barrel.IsDestroyed) return;
                    barrel.DropItems();
                    barrel.RemoveMe();
                }));
            }
            EmptyJunkPile(barrel);
        }

        private void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null) return;
            var entityID = lootContainer.net.ID;
            if (lootDestroyTimer.ContainsKey(entityID))
            {
                lootDestroyTimer[entityID]?.Destroy();
                lootDestroyTimer.Remove(entityID);
            }
            if (!entityPlayers.ContainsKey(entityID)) return;
            if (!configData.slapPlayer || Slap == null)
            {
                entityPlayers.Remove(entityID);
                return;
            }
            foreach (var playerID in entityPlayers[entityID])
            {
                var player = BasePlayer.FindByID(playerID);
                if (player == null || player.IPlayer == null) continue;
                Slap.Call("SlapPlayer", player.IPlayer);
                Print(player, Lang("SlapMessage", player.UserIDString));
            }
            entityPlayers.Remove(entityID);
        }

        private void OnEntityDeath(LootContainer lootContainer, HitInfo info)
        {
            if (lootContainer == null || lootContainer.net == null || info?.InitiatorPlayer == null) return;
            if (!lootContainer.ShortPrefabName.Contains("barrel")) return;
            var barrelID = lootContainer.net.ID;
            if (!entityPlayers.ContainsKey(barrelID)) return;
            var attacker = info.InitiatorPlayer;
            if (attacker.IsNpc || !attacker.userID.IsSteamId()) return;
            entityPlayers[barrelID].Remove(attacker.userID);
        }

        private void EmptyJunkPile(LootContainer lootContainer)
        {
            if (!configData.emptyJunkpile) return;
            var junkPiles = Facepunch.Pool.GetList<JunkPile>();
            Vis.Entities(lootContainer.transform.position, 6f, junkPiles);
            foreach (var junkPile in junkPiles)
            {
                if (junkPile != null && junkPile.net != null)
                {
                    var junkPileID = junkPile.net.ID;
                    if (!lootDestroyTimer.ContainsKey(junkPileID))
                    {
                        lootDestroyTimer.Add(junkPileID, timer.Once(configData.timeBeforeJunkpileEmpty, () =>
                        {
                            if (junkPile != null && !junkPile.IsDestroyed)
                            {
                                if (configData.dropNearbyLoot)
                                {
                                    var lootContainers = Facepunch.Pool.GetList<LootContainer>();
                                    Vis.Entities(junkPile.transform.position, 6f, lootContainers);
                                    foreach (var loot in lootContainers)
                                        DropUtil.DropItems(loot.inventory, loot.transform.position);
                                    Facepunch.Pool.FreeList(ref lootContainers);
                                }
                                junkPile.SinkAndDestroy();
                            }
                        }));
                    }
                    break;
                }
            }
            Facepunch.Pool.FreeList(ref junkPiles);
        }

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Time before the loot containers are empties (seconds)")]
            public float timeBeforeLootEmpty = 30f;

            [JsonProperty(PropertyName = "Empty the entire junkpile when automatically empty loot")]
            public bool emptyJunkpile = false;

            [JsonProperty(PropertyName = "Empty the nearby loot when emptying junkpile")]
            public bool dropNearbyLoot = false;

            [JsonProperty(PropertyName = "Time before the junkpile are empties (seconds)")]
            public float timeBeforeJunkpileEmpty = 150f;

            [JsonProperty(PropertyName = "Slaps players who don't empty containers")]
            public bool slapPlayer = false;

            [JsonProperty(PropertyName = "Chat prefix")]
            public string prefix = "[LootBouncer]:";

            [JsonProperty(PropertyName = "Chat prefix color")]
            public string prefixColor = "#00FFFF";

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong steamIDIcon = 0;

            [JsonProperty(PropertyName = "Loot container settings")]
            public Dictionary<string, bool> lootContainerSettings = new Dictionary<string, bool>();
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

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void Print(BasePlayer player, string message) => Player.Message(player, message, $"<color={configData.prefixColor}>{configData.prefix}</color>", configData.steamIDIcon);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SlapMessage"] = "You didn't empty the container. You got slapped by the container!!!",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SlapMessage"] = "WDNMD，不清空容器，给你个大耳刮子",
            }, this, "zh-CN");
        }

        #endregion LanguageFile
    }
}