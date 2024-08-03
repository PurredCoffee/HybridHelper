using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using IL.Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using On.Celeste;

namespace Celeste.Mod.HybridHelper;

public class HybridHelperModule : EverestModule
{
    public static HybridHelperModule Instance { get; private set; }

    public override Type SettingsType => typeof(HybridHelperModuleSettings);
    public static HybridHelperModuleSettings Settings => (HybridHelperModuleSettings)Instance._Settings;

    public override Type SessionType => typeof(HybridHelperModuleSession);
    public static HybridHelperModuleSession Session => (HybridHelperModuleSession)Instance._Session;

    public override Type SaveDataType => typeof(HybridHelperModuleSaveData);
    public static HybridHelperModuleSaveData SaveData => (HybridHelperModuleSaveData)Instance._SaveData;

    public HybridHelperModule()
    {
        Instance = this;
#if DEBUG
        // debug builds use verbose logging
        Logger.SetLogLevel(nameof(HybridHelperModule), LogLevel.Verbose);
#else
        // release builds use info logging to reduce spam in log files
        Logger.SetLogLevel(nameof(HybridHelperModule), LogLevel.Info);
#endif
    }

    static List<LevelData> levelsWithCheckpoints = new List<LevelData>();
    static List<LevelData> teleportLevels = new List<LevelData>();
    static List<LevelData> endingLevels = new List<LevelData>();
    static LevelData lastTeleportedLevel = null;
    static List<LevelData> OrderOfLevels = new List<LevelData>();
    public static MapData currentMapData = null;
    
    static Session.CoreModes originalCoreMode;
    static bool originalDreaming;
    private static bool ShouldTeleportPlayerToNextCheckpoint(LevelData playerLevel, Level currentLevel)
    {
        if(!Settings.RandomizeGoldenBerries || lastTeleportedLevel == playerLevel) {
            return false;
        }
        if(OrderOfLevels.Count == 0 || OrderOfLevels[0] == playerLevel) {
            return false;
        }
        bool foundLevel = false;
        foreach (LevelData level in endingLevels)
        {
            if (playerLevel == level)
            {
                foundLevel = true;
            }
        }
        if (!foundLevel)
        {
            return false;
        }
        foreach (Strawberry item in currentLevel.Entities.FindAll<Strawberry>())
        {
            if (item.Golden && item.Follower.Leader != null)
            {
                return true;
            }
        }
        return false;
    }

    private static void UpdateModesFromCheckpoint(Level level)
    {
        bool foundCheckpoint = false;
        foreach(CheckpointData checkpoint in currentMapData.ModeData.Checkpoints) {
            if(checkpoint.Level == lastTeleportedLevel.Name) {
                Logger.Log(LogLevel.Info, "HybridHelperModule", "Teleporting player to checkpoint: " + checkpoint.Name);
                Logger.Log(LogLevel.Info, "HybridHelperModule", "Inventory: " + ((checkpoint.Inventory != null)? "true" : "false"));
                Logger.Log(LogLevel.Info, "HybridHelperModule", "CoreMode: " + checkpoint.CoreMode);
                Logger.Log(LogLevel.Info, "HybridHelperModule", "Dreaming: " + checkpoint.Dreaming);
                if(checkpoint.Inventory != null) {
                    PlayerInventory inv = (PlayerInventory)checkpoint.Inventory;
                    level.Session.Inventory = inv;
                }
                if(checkpoint.CoreMode != null) {
                    level.coreMode = (Session.CoreModes)checkpoint.CoreMode;
                }
                if(!checkpoint.Dreaming) {
                    foreach(DreamBlock db in level.Entities.FindAll<DreamBlock>()) {
                        db.Activate();
                    }
                } else {
                    foreach(DreamBlock db in level.Entities.FindAll<DreamBlock>()) {
                        db.Deactivate();
                    }
                }
                foundCheckpoint = true;
            }
        }
        if(!foundCheckpoint) {
            Logger.Log(LogLevel.Warn, "HybridHelperModule", "No checkpoint found for level: " + lastTeleportedLevel.Name);
            level.Session.Inventory = currentMapData.ModeData.Inventory;
            level.coreMode = currentMapData.Data.CoreMode;
            if(!currentMapData.Data.Dreaming) {
                    foreach(DreamBlock db in level.Entities.FindAll<DreamBlock>()) {
                        db.Activate();
                    }
                } else {
                    foreach(DreamBlock db in level.Entities.FindAll<DreamBlock>()) {
                        db.Deactivate();
                    }
                }
        }
    }
    
    private static void TeleportPlayerToNextCheckpoint(Player player)
    {
        if (OrderOfLevels.Count == 0)
        {
            return;
        }
        player.Speed = Vector2.Zero;
        lastTeleportedLevel = OrderOfLevels[0];
        OrderOfLevels.RemoveAt(0);
        Level level = player.SceneAs<Level>();
        Vector2? spawnOffset = null;
        foreach(EntityData entity in lastTeleportedLevel.Entities) {
            if(entity.Name == "checkpoint") {
                spawnOffset = entity.Position;
                if(entity.Nodes?.Length > 0) {
                    spawnOffset = entity.Nodes[0];
                }
                break;
            }
        }
        UpdateModesFromCheckpoint(level);
        level.TeleportTo(player, lastTeleportedLevel.Name, Player.IntroTypes.Respawn, spawnOffset);
    }

    private static void UpdateLevelsToCheck()
    {
        teleportLevels.Clear();
        endingLevels.Clear();
        string levelName = currentMapData.Area.SID;
        foreach (LevelData level in levelsWithCheckpoints)
        {
            CheckpointHybridSetting setting = 
                Settings.DisableCheckpoint.TryGetValue(levelName + "/" + level.Name, out CheckpointHybridSetting value) ? value : CheckpointHybridSetting.Regular;
            switch (setting)
            {
                case CheckpointHybridSetting.Regular:
                    teleportLevels.Add(level);
                    endingLevels.Add(level);
                    break;
                case CheckpointHybridSetting.Ignore:
                    break;
                case CheckpointHybridSetting.Skip:
                    endingLevels.Add(level);
                    break;
            }
        }
        //final checkpoint is always the last room and should be included in the list of rooms to teleport to
        if(endingLevels.Count == 0 || endingLevels[endingLevels.Count-1] != levelsWithCheckpoints[levelsWithCheckpoints.Count-1]) {
            endingLevels.Add(levelsWithCheckpoints[levelsWithCheckpoints.Count-1]);
        }
        if(teleportLevels.Count == 0 || teleportLevels[teleportLevels.Count-1] != levelsWithCheckpoints[levelsWithCheckpoints.Count-1]) {
            teleportLevels.Add(levelsWithCheckpoints[levelsWithCheckpoints.Count-1]);
        }
    }
    
    private static void ShuffleOrderOfLevels(LevelData startLevel)
    {
        UpdateLevelsToCheck();
        OrderOfLevels.Clear();
        
        string levelName = currentMapData.Area.SID;
        CheckpointHybridSetting setting = 
            Settings.DisableCheckpoint.TryGetValue(levelName + "/Start", out CheckpointHybridSetting value) ? value : CheckpointHybridSetting.Regular;
        if(setting == CheckpointHybridSetting.Regular) {
            OrderOfLevels.Add(startLevel);
        }
        foreach (LevelData level in teleportLevels)
        {
            OrderOfLevels.Add(level);
        }
        //ensure the final CP is always the last room
        LevelData lastCP = OrderOfLevels[OrderOfLevels.Count-1];
        OrderOfLevels.RemoveAt(OrderOfLevels.Count-1);
        Random r = new Random();
        for(int i = 0; i < OrderOfLevels.Count; i++)
        {
            int j = r.Next(i, OrderOfLevels.Count);
            LevelData temp = OrderOfLevels[i];
            OrderOfLevels[i] = OrderOfLevels[j];
            OrderOfLevels[j] = temp;
        }
        OrderOfLevels.Add(lastCP);
    }
    
    public override void Load()
    {
        //hook for when a map is loaded
        On.Celeste.MapData.StartLevel += MapData_StartLevel;
        On.Celeste.Level.TransitionTo += Level_TransitionTo;
        On.Celeste.Strawberry.OnPlayer += Strawberry_OnPlayer;
        On.Celeste.Strawberry.Added += Strawberry_Added;
    }

    private static void Strawberry_Added(On.Celeste.Strawberry.orig_Added orig, Strawberry self, Scene scene)
    {
        orig(self, scene);
        if(!Settings.RandomizeGoldenBerries) {
            return;
        }
        int sameIDcount = 0;
        foreach (Strawberry b in ((Level)scene).Entities.FindAll<Strawberry>())
        {
            if (b.ID.ID == self.ID.ID)
            {
                sameIDcount++;
            }
        }
        if (sameIDcount > 1)
        {
            self.RemoveSelf();
        }
    }

    private static void Strawberry_OnPlayer(On.Celeste.Strawberry.orig_OnPlayer orig, Strawberry self, Player player)
    {
        //if enabled, not collected, and golden, randomize the order of checkpoints and teleport the player to the next checkpoint
        if(!Settings.RandomizeGoldenBerries || self.Follower.Leader != null || self.collected || !self.Golden) {
            orig(self, player);
            return;
        }
        ShuffleOrderOfLevels(self.SceneAs<Level>().Session.LevelData);
        if(OrderOfLevels[0] == self.SceneAs<Level>().Session.LevelData) {
            OrderOfLevels.RemoveAt(0);
            orig(self, player);
            return;
        }

        originalCoreMode = self.SceneAs<Level>().coreMode;
        originalDreaming = currentMapData.Data.Dreaming;
        
        new FadeWipe(player.SceneAs<Level>(), false, () => {
            //add current room to the list of levels with checkpoints
            self.SceneAs<Level>().OnEndOfFrame += () => TeleportPlayerToNextCheckpoint(player);
        }).Duration = 0.1f;
        orig(self, player);
    }

    private static LevelData MapData_StartLevel(On.Celeste.MapData.orig_StartLevel orig, MapData self)
    {
        LevelData returnobj = orig(self);
        currentMapData = self;
        //get array of checkpoints
        levelsWithCheckpoints.Clear();
        foreach (LevelData level in self.Levels)
        {
            if (level.HasCheckpoint) {
                levelsWithCheckpoints.Add(level);
            }
        }
        return returnobj;
    }

    static bool transitioning = false;
    private static void Level_TransitionTo(On.Celeste.Level.orig_TransitionTo orig, Level self, LevelData next, Vector2 direction)
    {
        //handled by ShouldTeleportPlayerToNextCheckpoint but to avoid future bugs, check if the setting is enabled
        if(!Settings.RandomizeGoldenBerries) {
            orig(self, next, direction);
            return;
        }
        if(!ShouldTeleportPlayerToNextCheckpoint(next, self)) {
            if(OrderOfLevels.Count > 0 && OrderOfLevels[0] == next) {
                OrderOfLevels.RemoveAt(0);
            }
            orig(self, next, direction);
            return;
        }
        if(next == lastTeleportedLevel) {
            orig(self, next, direction);
            return;
        }
        if(transitioning)
        {
            return;
        }
        transitioning = true;
        new FadeWipe(self, false, () => {
            self.OnEndOfFrame += () =>  {
                TeleportPlayerToNextCheckpoint(self.Tracker.GetEntity<Player>());
                transitioning = false;
            };
        }).Duration = 0.1f;
        // don't call orig, we're teleporting the player to the next checkpoint and dont transition to the next level
    }

    public override void Unload()
    {
        On.Celeste.MapData.StartLevel -= MapData_StartLevel;
        On.Celeste.Level.TransitionTo -= Level_TransitionTo;
        On.Celeste.Strawberry.OnPlayer -= Strawberry_OnPlayer;
        On.Celeste.Strawberry.Added -= Strawberry_Added;
    }
}