using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Loot Bouncer", "Sorrow", "0.1.1")]
    [Description("Empty the containers when players do not pick up all the items")]

    class LootBouncer : RustPlugin
    {
        Dictionary<uint, int> lootEntity = new Dictionary<uint, int>();
        private float _timeBeforeLootDespawn;

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;

            var entityId = entity.net.ID;
            var loot = entity.GetComponent<LootContainer>();
            if (loot == null) return;
            lootEntity.Add(entityId, loot.inventory.itemList.Count);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;

            var entityId = entity.net.ID;
            var loot = entity.GetComponent<LootContainer>();
            if (loot == null) return;

            var originalValue = 0;
            if (lootEntity.TryGetValue(entityId, out originalValue))
            {
                if (loot.inventory.itemList.Count != originalValue)
                {
                    timer.Once(_timeBeforeLootDespawn, () =>
                    {
                        if (loot == null) return;
                        DropUtil.DropItems(loot?.inventory, loot.transform.position);
                        BaseNetworkable.serverEntities.Find(entityId)?.Kill();
                    });
                }
                lootEntity.Remove(entityId);
            }
        }

        private new void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            Config.Clear();

            Config["Time before the loot despawn in seconds"] = 10;

            SaveConfig();
        }

        private void OnServerInitialized()
        {
            _timeBeforeLootDespawn = Convert.ToInt32(Config["Time before the loot despawn in seconds"]);
        }
    }
}