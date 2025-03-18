# LootBouncer

Oxide plugin for Rust. Empty the containers when players do not pick up all the items

## Configuration

```json
{
  "Time before the loot containers are empties (seconds)": 30.0,//once an item has been remove from a crate - timer before create disappears
  "Empty the entire junkpile when automatically empty loot": false,//once a crate or barrel is destroyed - does the entire junkpile disappear
  "Empty the nearby loot when emptying junkpile": false,//if the junkpile is destroyed is the loot from the remaining crates and barrels dropped
  "Time before the junkpile are empties (seconds)": 150.0,//once a crate or barrel is destroyed - timer before the entire junkpile disappears
  "Slaps players who don't empty containers": false,
  "Remove instead bouncing": false,//If true, the loot container items will no longer bounce
  "Chat prefix": "[LootBouncer]:",
  "Chat prefix color": "#00FFFF",
  "Chat steamID icon": 0,
  "Loot container settings": {       // If false, the container will not be emptied
    "loot-barrel-1": true,
    "loot-barrel-2": true,
    "trash-pile-1": true
  }
}
```

## Credits

**Sorrow**: original author of plugin
**Arainrr**: previous maintainer
