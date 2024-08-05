using System;
using System.Collections.Generic;

namespace Celeste.Mod.HybridHelper;

public enum CheckpointHybridSetting {
    Regular = 0,
    Ignore = 1,
    Skip = 2
}

public class HybridHelperModuleSettings : EverestModuleSettings {
    [SettingSubText("Ensure that you have paceping disabled")]
    public bool RandomizeGoldenBerries { get; set; } = false;

    [SettingSubHeader("Disable Checkpoints")]
    public Dictionary<string, CheckpointHybridSetting> DisableCheckpoint { get; set; } = new Dictionary<string, CheckpointHybridSetting>();

    public void CreateDisableCheckpointEntry(TextMenu menu, bool inGame) {
        if(!inGame) {
            return;
        }
        new DisableDisplayHandler(menu);
    }
}

public class DisableDisplayHandler {
    public void createDisableBool(TextMenuExt.SubMenu menu, string name, string displayName, bool forceState = false, CheckpointHybridSetting forceValue = CheckpointHybridSetting.Regular) {
        if(forceState) {
            HybridHelperModule.Settings.DisableCheckpoint[name] = forceValue;
        }
        CheckpointHybridSetting setting = HybridHelperModule.Settings.DisableCheckpoint.TryGetValue(name, out CheckpointHybridSetting value) ? value : CheckpointHybridSetting.Regular;
        TextMenuExt.EnumSlider<CheckpointHybridSetting> slider = new TextMenuExt.EnumSlider<CheckpointHybridSetting>(displayName, setting)
        {
            OnValueChange = (CheckpointHybridSetting value) =>
            {
                HybridHelperModule.Settings.DisableCheckpoint[name] = value;
            }
        };
        if(forceState) {
            slider.Disabled = true;
        }
        menu.Add(slider);
    }
    
    public DisableDisplayHandler(TextMenu menu) {
        if(HybridHelperModule.currentMapData == null) {
            return;
        }
        string levelName = HybridHelperModule.currentMapData.Area.SID;
        if(levelName.StartsWith("Celeste_")) {
            levelName = levelName.Substring(8);
        }
        string levelDisplayName = Dialog.Clean(levelName);
        TextMenuExt.SubMenu subMenu = new TextMenuExt.SubMenu(levelDisplayName + " Checkpoints", false);
        createDisableBool(subMenu, levelName + "/Start", "Start");
        for (int i = 0; i < HybridHelperModule.currentMapData.ModeData.Checkpoints.Length; i++) {
            CheckpointData checkpoint = HybridHelperModule.currentMapData.ModeData.Checkpoints[i];
            string checkpointName = checkpoint.Name;
            string localizedCheckpointName = Dialog.Clean(checkpointName);
            if(i == HybridHelperModule.currentMapData.ModeData.Checkpoints.Length - 1) {
                createDisableBool(subMenu, levelName + "/" + checkpoint.Level, localizedCheckpointName, true, CheckpointHybridSetting.Regular);
            }
            else {
                createDisableBool(subMenu, levelName + "/" + checkpoint.Level, localizedCheckpointName);
            }
        }
        menu.Add(subMenu);
    }
}