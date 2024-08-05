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

    private static bool HasGoldenBerry(Player player)
    {
        return player.Leader.Followers.FirstOrDefault((Follower f) => f.Entity.GetType() == typeof(Strawberry) && (f.Entity as Strawberry).Golden && !(f.Entity as Strawberry).Winged) != null;
    }

    private static bool HasPlatinumBerry(Player player)
    {
        foreach (Follower follower in player.Leader.Followers)
        {
            if (follower.Entity.GetType().Name == "PlatinumBerry")
            {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldTeleportPlayerToNextCheckpoint(LevelData playerLevel, Player player)
    {
        if(!Settings.RandomizeGoldenBerries) {
            return false;
        }
        if (lastTeleportedLevel == playerLevel) {
            return false;
        }
        if(OrderOfLevels.Count == 0) {
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
        if (HasGoldenBerry(player) || HasPlatinumBerry(player))
        {
            return true;
        }
        return false;
    }

    private static void UpdateModesFromCheckpoint(Level level)
    {
        bool foundCheckpoint = false;
        foreach(CheckpointData checkpoint in currentMapData.ModeData.Checkpoints) {
            if(checkpoint.Level == lastTeleportedLevel.Name) {
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
        Level level = player.SceneAs<Level>();
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

    public static bool teleporting = false;
    private static bool initateTeleport(Player player, Level level, LevelData next)
    {
        if(teleporting) {
            return true;
        }
        teleporting = true;
        if(OrderOfLevels.Count > 0 && OrderOfLevels[0] == next) {
            lastTeleportedLevel = OrderOfLevels[0];
            OrderOfLevels.RemoveAt(0);
            teleporting = false;
            return false;
        } else {
            new FadeWipe(level, false, () => {
                level.OnEndOfFrame += () =>  {
                    TeleportPlayerToNextCheckpoint(player);
                    teleporting = false;
                };
            }).Duration = 0.1f;
            return true;
        }
    }

    public override void Load()
    {
        //hook for when a map is loaded
        On.Celeste.MapData.StartLevel += MapData_StartLevel;
        On.Celeste.Level.TransitionTo += Level_TransitionTo;
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.Strawberry.Added += Strawberry_Added;
    }

    static bool hadGoldenBerryLastFrame = false;
    static int goldenBerryLenience = 0;
    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if(!Settings.RandomizeGoldenBerries || currentMapData == null) {
            return;
        }
        Player player = self.Tracker.GetEntity<Player>();
        if(player == null) {
            return;
        }
        bool hasGoldenBerry = HasGoldenBerry(player) || HasPlatinumBerry(player);

        if(!hadGoldenBerryLastFrame && hasGoldenBerry) {
            ShuffleOrderOfLevels(self.Session.LevelData);
            originalCoreMode = self.coreMode;
            originalDreaming = currentMapData.Data.Dreaming;

            initateTeleport(player, self, self.Session.LevelData);
        }
        //player can have frames where they have a golden berry but it doesn't count as having it yet
        //this is to prevent the player from being counted as not having a golden berry when they do
        if(goldenBerryLenience > 0) {
            goldenBerryLenience--;
        }
        if(hasGoldenBerry) {
            goldenBerryLenience = 5;
            hadGoldenBerryLastFrame = true;
        }
        if(!hasGoldenBerry && goldenBerryLenience == 0) {
            hadGoldenBerryLastFrame = false;
        }
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

    private static void Level_TransitionTo(On.Celeste.Level.orig_TransitionTo orig, Level self, LevelData next, Vector2 direction)
    {
        if(!Settings.RandomizeGoldenBerries || !ShouldTeleportPlayerToNextCheckpoint(next, self.Tracker.GetEntity<Player>()) || next == lastTeleportedLevel) {    
            orig(self, next, direction);
            return;
        }
        if(!initateTeleport(self.Tracker.GetEntity<Player>(), self, next)) {
            orig(self, next, direction);
        }
    }

    public override void Unload()
    {
        On.Celeste.MapData.StartLevel -= MapData_StartLevel;
        On.Celeste.Level.TransitionTo -= Level_TransitionTo;
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.Strawberry.Added -= Strawberry_Added;
    }
}