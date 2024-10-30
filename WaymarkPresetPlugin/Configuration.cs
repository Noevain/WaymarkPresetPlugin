using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Dalamud.Configuration;
using Newtonsoft.Json;
using WaymarkPresetPlugin.Subscription;

namespace WaymarkPresetPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public Configuration()
    {
        GetZoneNameDelegate = GetZoneNameHelperFunc;
        this.SubscriptionManager = new SubscriptionManager(this);
    }

    //  Our own configuration options and data.
    public WaymarkPresetLibrary PresetLibrary { get; protected set; } = new();

    [JsonIgnore]
    public SubscriptionManager SubscriptionManager { get; protected set; }
    public List<SubscriptionRepo> Subscriptions { get; protected set; } = new();
    
    public ConcurrentDictionary<string,string> url_to_etags { get; set; } = new();
    //	Need a real backing field on the following properties for use with ImGui.
    public bool mSortPresetsByZone = true;

    public bool SortPresetsByZone
    {
        get { return mSortPresetsByZone; }
        set { mSortPresetsByZone = value; }
    }

    public bool mAlwaysShowInfoPane = false;

    public bool AlwaysShowInfoPane
    {
        get { return mAlwaysShowInfoPane; }
        set { mAlwaysShowInfoPane = value; }
    }

    public bool mAllowUnselectPreset = false;

    public bool AllowUnselectPreset
    {
        get { return mAllowUnselectPreset; }
        set { mAllowUnselectPreset = value; }
    }
    
    public bool mAutoCheckForUpdates = true;

    public bool AutoCheckForUpdates
    {
        get { return mAutoCheckForUpdates; }
        set { mAutoCheckForUpdates = value; }
    }

    public int _minutesBetweenAutoCheckForUpdates = 120;

    public int MinuteBetweenAutoCheckForUpdates
    {
        get { return _minutesBetweenAutoCheckForUpdates; }
        set { _minutesBetweenAutoCheckForUpdates = value; }
    }

    public bool mFilterOnCurrentZone = false;

    public bool FilterOnCurrentZone
    {
        get { return mFilterOnCurrentZone; }
        set { mFilterOnCurrentZone = value; }
    }

    public bool mShowIDNumberNextToZoneNames = false;

    public bool ShowIDNumberNextToZoneNames
    {
        get { return mShowIDNumberNextToZoneNames; }
        set { mShowIDNumberNextToZoneNames = value; }
    }

    public bool mShowLibraryIndexInPresetList = false;

    public bool ShowLibraryIndexInPresetInfo
    {
        get { return mShowLibraryIndexInPresetList; }
        set { mShowLibraryIndexInPresetList = value; }
    }

    /*public bool mAllowClientSidePlacementInOverworldZones = false;
    public bool AllowClientSidePlacementInOverworldZones
    {
        get { return mAllowClientSidePlacementInOverworldZones && mAllowDirectPlacePreset; }
        set { mAllowClientSidePlacementInOverworldZones = value; }
    }*/

    public bool mAutoPopulatePresetsOnEnterInstance = false;

    public bool AutoPopulatePresetsOnEnterInstance
    {
        get { return mAutoPopulatePresetsOnEnterInstance; }
        set { mAutoPopulatePresetsOnEnterInstance = value; }
    }

    public bool mAutoSavePresetsOnInstanceLeave = false;

    public bool AutoSavePresetsOnInstanceLeave
    {
        get { return mAutoSavePresetsOnInstanceLeave; }
        set { mAutoSavePresetsOnInstanceLeave = value; }
    }

    public bool mSuppressCommandLineResponses = false;

    public bool SuppressCommandLineResponses
    {
        get { return mSuppressCommandLineResponses; }
        set { mSuppressCommandLineResponses = value; }
    }

    public bool mOpenAndCloseWithFieldMarkerAddon = false;

    public bool OpenAndCloseWithFieldMarkerAddon
    {
        get { return mOpenAndCloseWithFieldMarkerAddon; }
        set { mOpenAndCloseWithFieldMarkerAddon = value; }
    }

    public bool mAttachLibraryToFieldMarkerAddon = false;

    public bool AttachLibraryToFieldMarkerAddon
    {
        get { return mAttachLibraryToFieldMarkerAddon; }
        set { mAttachLibraryToFieldMarkerAddon = value; }
    }

    public bool mAllowPresetDragAndDropOrdering = true;

    public bool AllowPresetDragAndDropOrdering
    {
        get { return mAllowPresetDragAndDropOrdering; }
        set { mAllowPresetDragAndDropOrdering = value; }
    }

    public bool mAllowZoneDragAndDropOrdering = true;

    public bool AllowZoneDragAndDropOrdering
    {
        get { return mAllowZoneDragAndDropOrdering; }
        set { mAllowZoneDragAndDropOrdering = value; }
    }

    public bool mSortZonesDescending = false;

    public bool SortZonesDescending
    {
        get { return mSortZonesDescending; }
        set { mSortZonesDescending = value; }
    }

    public bool mShowLibraryZoneFilterBox = true;

    public bool ShowLibraryZoneFilterBox
    {
        get { return mShowLibraryZoneFilterBox; }
        set { mShowLibraryZoneFilterBox = value; }
    }

    public string GetZoneName(ushort ID)
    {
        return GetZoneNameDelegate(ID, ShowIDNumberNextToZoneNames);
    }

    [JsonIgnore] public WaymarkPreset.GetZoneNameDelegate GetZoneNameDelegate { get; protected set; }

    protected string GetZoneNameHelperFunc(ushort ID, bool showID)
    {
        /*ShowDutyNames ?*/
        /*: ZoneInfoHandler.GetZoneInfoFromContentFinderID( ID ).ZoneName*/
        return $"{ZoneInfoHandler.GetZoneInfoFromContentFinderID(ID).DutyName}{(showID ? $" ({ID})" : "")}";
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    public void BackupConfigFile()
    {
        var backupFolderPath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), "Backups");
        Directory.CreateDirectory(backupFolderPath);
        var backupFilePath = GetBackupFilePath(backupFolderPath, "PluginConfig", "json");
        Plugin.PluginInterface.ConfigFile.CopyTo(backupFilePath);
    }

    internal void BackupConfigFolderFile(string fileName, string extension)
    {
        var backupFolderPath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), "Backups");
        Directory.CreateDirectory(backupFolderPath);

        if (File.Exists(Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), $"{fileName}.{extension}")))
        {
            var backupFilePath = GetBackupFilePath(backupFolderPath, fileName, extension);
            var fileToCopy = new FileInfo(Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), $"{fileName}.{extension}"));
            fileToCopy?.CopyTo(backupFilePath);
        }
    }

    internal static string GetBackupFilePath(string folderPath, string fileName, string extension)
    {
        var fileBasePath = Path.Join(folderPath, $"{fileName}_{DateTimeOffset.UtcNow:yyyy-MM-dd_HH.mm.ssZ}");
        var filePath = fileBasePath + $".{extension}";
        var i = 2;
        while (File.Exists(filePath))
        {
            filePath = fileBasePath + $"_{i}.{extension}";
            ++i;
        }

        return filePath;
    }
}