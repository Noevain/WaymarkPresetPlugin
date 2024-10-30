using System;
using CheapLoc;
using Dalamud.Utility;
using ImGuiNET;

namespace WaymarkPresetPlugin.UI;

internal sealed class WindowSettings : IDisposable
{
    private bool mWindowVisible = false;

    public bool WindowVisible
    {
        get { return mWindowVisible; }
        set { mWindowVisible = value; }
    }

    private bool mWantToDeleteMapViewData = false;
    private bool mWantToDeleteZoneSortData = false;

    private readonly PluginUI PluginUI;
    private readonly Configuration Configuration;

    public WindowSettings(PluginUI UI, Configuration configuration)
    {
        PluginUI = UI;
        Configuration = configuration;
    }

    public void Dispose()
    {
    }

    public void Draw()
    {
        if (!WindowVisible)
        {
            mWantToDeleteMapViewData = false;
            mWantToDeleteZoneSortData = false;
            return;
        }

        if (ImGui.Begin(Loc.Localize("Window Title: Config", "Waymark Settings") + "###Waymark Settings", ref mWindowVisible, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.Checkbox(Loc.Localize("Config Option: Always Show Info Pane", "Always show preset info pane.") + "###Always show preset info pane checkbox", ref Configuration.mAlwaysShowInfoPane);
            ImGui.Checkbox(Loc.Localize("Config Option: Clicking Preset Unselects", "Clicking the selected preset unselects it.") + "###Clicking the selected preset unselects it checkbox", ref Configuration.mAllowUnselectPreset);
            ImGui.Checkbox(Loc.Localize("Config Option: Categorize Presets by Zone", "Categorize presets by zone.") + "###Categorize Presets By Zone Checkbox", ref Configuration.mSortPresetsByZone);
            ImGui.Checkbox(Loc.Localize("Config Option: Open and Close with Game Window", "Open and close library with the game's waymark window.") + "###Open and Close With Game Window checkbox", ref Configuration.mOpenAndCloseWithFieldMarkerAddon);
            ImGui.Checkbox(Loc.Localize("Config Option: Attach to Game Window", "Attach library window to the game's waymark window.") + "###Attach library window to the game's waymark window checkbox.", ref Configuration.mAttachLibraryToFieldMarkerAddon);
            ImGui.Checkbox(Loc.Localize("Config Option: Show ID in Zone Names", "Show ID numbers next to zone names.") + "###Show ID numbers next to zone names checkbox.", ref Configuration.mShowIDNumberNextToZoneNames);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Show ID in Zone Names", "Shows the internal Content Finder ID of the zone/duty in some places.  Generally only used for debugging."));
            ImGui.Checkbox(Loc.Localize("Config Option: Show Preset Indices", "Show the index of the preset within the library.") + "###Show the index of the preset within the library checkbox", ref Configuration.mShowLibraryIndexInPresetList);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Show Preset Indices", "The primary use of this is if you need to know the preset index to use within a text command.  You can always leave this disabled if you only use the GUI."));
            /*ImGui.Checkbox( "Allow placement of waymarks client-side in overworld zones.", ref mConfiguration.mAllowClientSidePlacementInOverworldZones );
            ImGuiUtils.HelpMarker( "Lets the plugin attempt to place waymarks in overworld zones that do not function with the game's preset interface.  These will only be visible client-side, and not to other party/alliance members.  This is out of specification behavior for the game, so please read this plugin's readme before enabling." );*/
            ImGui.Checkbox(Loc.Localize("Config Option: Allow Preset Drag and Drop", "Allow drag and drop reordering of presets.") + "###Allow drag and drop reordering of presets checkbox", ref Configuration.mAllowPresetDragAndDropOrdering);
            ImGui.Checkbox(Loc.Localize("Config Option: Allow Zone Drag and Drop", "Allow drag and drop reordering of zones.") + "###Allow drag and drop reordering of zones checkbox", ref Configuration.mAllowZoneDragAndDropOrdering);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Allow Zone Drag and Drop", "If this is enabled, you can change the order of preset folders sorted by zone.  New zones will be added at the bottom of the list.  This option has no effect if the \"Categorize presets by zone\" option is disabled."));
            ImGui.Checkbox(Loc.Localize("Config Option: Sort Zones Descending", "Sort zones descending (newest at the top).") + "###Sort zones descending checkbox", ref Configuration.mSortZonesDescending);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Sort Zones Descending", "When this is checked, zones will be sorted roughly (SE's ordering isn't perfect) newest to oldest.  When coupled with the \"Allow drag and drop reordering of zones\" option, new zones will be added at the top of the list (with everything else remaining in the same order below)."));
            ImGui.Checkbox(Loc.Localize("Config Option: Show Library Zone Filter Search Box", "Show search box to filter zones in library window.") + "###Show Library Zone Filter Input", ref Configuration.mShowLibraryZoneFilterBox);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Show Library Zone Filter Search Box", "When this is checked, a search box will be shown at the top of the library window to filter the zones you want to see.  This has no effect when the \"Categorize presets by zone\" option is disabled."));
            ImGui.Checkbox(Loc.Localize("Config Option: Autoload Presets from Libarary", "Autoload presets from library.") + "###Autoload presets from library checkbox", ref Configuration.mAutoPopulatePresetsOnEnterInstance);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Autoload presets from Library", "Automatically loads the first five presets that exist in the library for a zone when you load into it.  THIS WILL OVERWRITE THE GAME'S SLOTS WITHOUT WARNING, so please do not turn this on until you are certain that you have saved any data that you want to keep.  Consider using this with the auto-import option below to reduce the risk of inadvertent preset loss."));
            ImGui.Checkbox(Loc.Localize("Config Option: Autosave Presets to Library", "Autosave presets to library.") + "###Autosave Presets to Library Checkbox", ref Configuration.mAutoSavePresetsOnInstanceLeave);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Autosave Presets to Library", "Automatically copies any populated game preset slots into the library upon exiting an instance."));
            ImGui.Checkbox(Loc.Localize("Config Option: Autocheck for Updates","Check for updates automatically") + "###Autocheck for updates checkbox", ref Configuration.mAutoCheckForUpdates);
            ImGuiUtils.HelpMarker(Loc.Localize("Help: Automatically check for updates every X minutes", "Automatically check for updates according to the below value in minutes. This will NOT update automatically,it will just check. Press the update button to update"));
            int minutesBetweenAutoUpdateCheck = Configuration.MinuteBetweenAutoCheckForUpdates;
            string buffer = minutesBetweenAutoUpdateCheck.ToString();
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputText("Time in minutes between auto-update check", ref buffer, 255,
                    ImGuiInputTextFlags.CharsDecimal))
            {
                if (int.TryParse(buffer, out var parsedInt))
                {
                    Configuration.MinuteBetweenAutoCheckForUpdates = Math.Clamp(parsedInt,1,600);
                }
            }
            ImGui.Checkbox(Loc.Localize("Config Option: Suppress Text Command Responses", "Suppress responses to text commands (besides \"{0}\").").Format(Plugin.SubcommandHelp) + "###Suppress Command Responses Checkbox", ref Configuration.mSuppressCommandLineResponses);
            ImGui.Spacing();
            if (ImGui.Button(Loc.Localize("Button: Clear All Map View Data", "Clear All Map View Data") + "###Clear All Map View Data Button"))
                mWantToDeleteMapViewData = true;

            ImGuiUtils.HelpMarker(Loc.Localize("Help: Clear All Map View Data", "This deletes all map view pan/zoom/submap state, resetting every map back to default."));
            if (mWantToDeleteMapViewData)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, 0xee4444ff);
                ImGui.Text(Loc.Localize("Settings Window Text: Confirm Delete Label", "Confirm delete: "));
                ImGui.SameLine();
                if (ImGui.Button(Loc.Localize("Button: Yes", "Yes") + "###Delete Map View Data Yes Button"))
                {
                    PluginUI.MapWindow.ClearAllMapViewStateData();
                    mWantToDeleteMapViewData = false;
                }

                ImGui.PopStyleColor();
                ImGui.SameLine();
                if (ImGui.Button(Loc.Localize("Button: No", "No") + "###Delete Map View Data No Button"))
                    mWantToDeleteMapViewData = false;
            }

            ImGui.Spacing();
            if (ImGui.Button(Loc.Localize("Button: Clear All Zone Sort Data", "Clear All Zone Sort Data") + "###Clear All Zone Sort Data Button"))
                mWantToDeleteZoneSortData = true;

            ImGuiUtils.HelpMarker(Loc.Localize("Help: Clear All Zone Sort Data", "This deletes any custom ordering of the zones in the library window, and resets the sort order back to default."));
            if (mWantToDeleteZoneSortData)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, 0xee4444ff);
                ImGui.Text(Loc.Localize("Settings Window Text: Confirm Delete Label", "Confirm delete: "));
                ImGui.SameLine();
                if (ImGui.Button(Loc.Localize("Button: Yes", "Yes") + "###Delete Zone Sort Data Yes Button"))
                {
                    PluginUI.LibraryWindow.ClearAllZoneSortData();
                    mWantToDeleteZoneSortData = false;
                }

                ImGui.PopStyleColor();
                ImGui.SameLine();
                if (ImGui.Button(Loc.Localize("Button: No", "No") + "###Delete Zone Sort Data No Button"))
                {
                    mWantToDeleteZoneSortData = false;
                }
            }

            ImGui.Spacing();
            if (ImGui.Button(Loc.Localize("Button: Save and Close", "Save and Close") + "###Save and Close Button"))
            {
                Configuration.Save();
                WindowVisible = false;
            }

            var showLibraryButtonString = Loc.Localize("Button: Show Library", "Show Library");
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(showLibraryButtonString).X - ImGui.GetStyle().FramePadding.X * 2);
            if (ImGui.Button(showLibraryButtonString + "###Show Library Button"))
                PluginUI.LibraryWindow.WindowVisible = true;
        }

        ImGui.End();
    }
}