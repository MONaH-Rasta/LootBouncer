using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Loot Bouncer", "Sorrow/Arainrr", "1.0.2")]
    [Description("Empty the containers when players do not pick up all the items")]
    internal class LootBouncer : RustPlugin
    {
        [PluginReference] private readonly Plugin Slap, Trade;
        private readonly Dictionary<uint, int> lootEntities = new Dictionary<uint, int>();
        private readonly Dictionary<uint, HashSet<ulong>> entityPlayers = new Dictionary<uint, HashSet<ulong>>();
        private readonly Dictionary<uint, Timer> lootDestroyTimer = new Dictionary<uint, Timer>();

        private readonly List<string> barrels = new List<string>
        {
            "loot-barrel-1",
            "loot-barrel-2",
            "loot_barrel_1",
            "loot_barrel_2",
            "oil_barrel",
        };

        private void Unload()
        {
            foreach (var entry in lootDestroyTimer)
                entry.Value?.Destroy();
        }

        private void OnLootEntity(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer?.net == null || player == null) return;
            var result = Trade?.Call("IsTradeBox", lootContainer);
            if (result != null && result is bool && (bool)result) return;
            if (lootContainer?.inventory?.itemList == null || lootContainer?.SpawnType == null) return;

            if (LootContainer.spawnType.AIRDROP.Equals(lootContainer.SpawnType) && !configData.emptyAirdrop) return;
            if (LootContainer.spawnType.CRASHSITE.Equals(lootContainer.SpawnType) && !configData.emptyCrashsite) return;
            if (lootContainer is HackableLockedCrate && !configData.emptyHackableCrate) return;

            var entityId = lootContainer.net.ID;
            if (!lootEntities.ContainsKey(entityId)) lootEntities.Add(entityId, lootContainer.inventory.itemList.Count);

            if (!entityPlayers.ContainsKey(entityId)) entityPlayers.Add(entityId, new HashSet<ulong> { player.userID });
            else entityPlayers[entityId].Add(player.userID);
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer?.net == null || player == null) return;
            if (lootContainer?.inventory?.itemList == null || lootContainer?.SpawnType == null) return;
            var entityId = lootContainer.net.ID;
            if (lootContainer.inventory.itemList.Count <= 0)
            {
                if (entityPlayers.ContainsKey(entityId)) entityPlayers[entityId].Remove(player.userID);
                if (lootEntities.ContainsKey(entityId)) lootEntities.Remove(entityId);
                return;
            }
            if (lootEntities.ContainsKey(entityId) && entityPlayers.ContainsKey(entityId))
            {
                if (lootContainer.inventory.itemList.Count < lootEntities[entityId])
                {
                    if (!lootDestroyTimer.ContainsKey(entityId))
                    {
                        lootDestroyTimer.Add(entityId, timer.Once(configData.timeBeforeLootEmpty, () =>
                        {
                            if (lootContainer?.inventory?.itemList == null) return;
                            DropUtil.DropItems(lootContainer.inventory, lootContainer.transform.position);
                            lootContainer.Die();
                        }));
                    }
                }
                else entityPlayers[entityId].Remove(player.userID);
                EmptyJunkPile(lootContainer);
                lootEntities.Remove(entityId);
            }
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || info?.HitEntity == null) return;
            if (attacker.IsNpc || !attacker.userID.IsSteamId()) return;
            if (barrels.Contains(info.HitEntity.ShortPrefabName))
            {
                var barrel = info.HitEntity as LootContainer;
                if (barrel?.net?.ID == null) return;
                var barrelId = barrel.net.ID;

                if (!entityPlayers.ContainsKey(barrelId)) entityPlayers.Add(barrelId, new HashSet<ulong> { attacker.userID });
                else entityPlayers[barrelId].Add(attacker.userID);

                if (!lootDestroyTimer.ContainsKey(barrelId))
                {
                    lootDestroyTimer.Add(barrelId, timer.Once(configData.timeBeforeLootEmpty, () =>
                    {
                        if (barrel?.inventory?.itemList == null) return;
                        DropUtil.DropItems(barrel.inventory, barrel.transform.position);
                        barrel.Die();
                    }));
                }
                EmptyJunkPile(barrel);
            }
        }

        private void EmptyJunkPile(LootContainer lootContainer)
        {
            if (configData.emptyJunkpile)
            {
                var junkPiles = Facepunch.Pool.GetList<JunkPile>();
                Vis.Entities(lootContainer.transform.position, 5f, junkPiles);
                if (junkPiles.Count > 0)
                {
                    var junkPile = junkPiles[0];
                    if (junkPile?.net?.ID != null)
                    {
                        var junkPileId = junkPile.net.ID;
                        if (!lootDestroyTimer.ContainsKey(junkPileId))
                        {
                            lootDestroyTimer.Add(junkPileId, timer.Once(configData.timeBeforeJunkpileEmpty, () =>
                            {
                                if (junkPile == null || junkPile?.IsDestroyed == true) return;
                                if (configData.dropNearbyLoot)
                                {
                                    var lootContainers = Facepunch.Pool.GetList<LootContainer>();
                                    Vis.Entities(junkPile.transform.position, 5f, lootContainers);
                                    foreach (var loot in lootContainers)
                                        DropUtil.DropItems(loot.inventory, loot.transform.position);
                                    Facepunch.Pool.FreeList(ref lootContainers);
                                }
                                junkPile.SinkAndDestroy();
                            }));
                        }
                    }
                }
                Facepunch.Pool.FreeList(ref junkPiles);
            }
        }

        private void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer?.net?.ID == null) return;
            var entityId = lootContainer.net.ID;
            if (lootDestroyTimer.ContainsKey(entityId))
            {
                lootDestroyTimer[entityId]?.Destroy();
                lootDestroyTimer.Remove(entityId);
            }
            if (!entityPlayers.ContainsKey(entityId)) return;
            if (!configData.slapPlayer || Slap == null)
            {
                entityPlayers.Remove(entityId);
                return;
            }
            bool isBarrel = false;
            if (barrels.Contains(lootContainer.ShortPrefabName)) isBarrel = true;
            foreach (var playerID in entityPlayers[entityId])
            {
                var player = BasePlayer.FindByID(playerID);
                if (player?.IPlayer == null) continue;
                Slap?.Call("SlapPlayer", player.IPlayer);
                if (isBarrel) Print(player, Lang("Barrel slap", player.UserIDString));
                else Print(player, Lang("Container slap", player.UserIDString));
            }
        }

        private void OnEntityDeath(LootContainer lootContainer, HitInfo info)
        {
            if (lootContainer?.net?.ID == null || info?.InitiatorPlayer == null) return;
            if (!barrels.Contains(lootContainer.ShortPrefabName)) return;
            var attacker = info.InitiatorPlayer;
            if (attacker.IsNpc || !attacker.userID.IsSteamId()) return;
            var barrelId = lootContainer.net.ID;
            if (entityPlayers.ContainsKey(barrelId)) entityPlayers[barrelId].Remove(attacker.userID);
        }

        private void OnServerInitialized()
        {
            if (!configData.emptyBarrel)
            {
                Unsubscribe(nameof(OnPlayerAttack));
                Unsubscribe(nameof(OnEntityDeath));
            }
            if (configData.slapPlayer && Slap == null)
                PrintError("Slap is not loaded, get it at https://umod.org/plugins/slap");
        }

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Time before the loot containers are empties")]
            public float timeBeforeLootEmpty = 30f;

            [JsonProperty(PropertyName = "Empty the entire junkpile")]
            public bool emptyJunkpile = false;

            [JsonProperty(PropertyName = "Time before the junkpile are empties")]
            public float timeBeforeJunkpileEmpty = 150f;

            [JsonProperty(PropertyName = "Empty the nearby loot when emptying junkpile")]
            public bool dropNearbyLoot = false;

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

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void Print(BasePlayer player, string message) => Player.Message(player, message, $"<color={configData.prefixColor}>{configData.prefix}</color>", configData.steamIDIcon);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Container slap"] = "You didn't empty the container. Slap you in the face",
                ["Barrel slap"] = "You didn't killed the barrel. Slap you in the face"
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Container slap"] = "WDNMD，不清空箱子，给你个大耳刮子",
                ["Barrel slap"] = "WDNMD，不摧毁桶子，给你个大耳刮子"
            }, this, "zh-CN");
        }

        #endregion LanguageFile
    }
}