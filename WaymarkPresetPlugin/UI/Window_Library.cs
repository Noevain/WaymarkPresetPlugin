using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CheapLoc;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Newtonsoft.Json;
using WaymarkPresetPlugin.Subscription;

namespace WaymarkPresetPlugin.UI;

internal sealed class WindowLibrary : IDisposable
{
    private bool mWindowVisible = false;

    public bool WindowVisible
    {
        get { return mWindowVisible; }
        set { mWindowVisible = value; }
    }

    public Vector2 WindowPos { get; private set; }
    public Vector2 WindowSize { get; private set; }

    private readonly PluginUI PluginUI;
    private readonly Configuration Configuration;

    private string mPresetImportString = "";
    private string mUrlImportString = "";

    public string PresetImportString
    {
        get { return mPresetImportString; }
        set { mPresetImportString = value; }
    }

    public int SelectedPreset { get; private set; } = -1;

    private uint mGameSlotDropdownSelection = 1;
    
    private Task subTask = null;
    private Exception subEx = null;
    private bool FieldMarkerAddonWasOpen { get; set; } = false;

    private readonly IntPtr mpLibraryZoneDragAndDropData;
    private readonly IntPtr mpLibraryPresetDragAndDropData;

    private ZoneSearcher LibraryWindowZoneSearcher { get; set; } = new ZoneSearcher();
    private string mSearchText = "";

    internal const string mZoneSortDataFileName_v1 = "LibraryZoneSortData_v1.json";

    public WindowLibrary(PluginUI UI, Configuration configuration, IntPtr pLibraryZoneDragAndDropData, IntPtr pLibraryPresetDragAndDropData)
    {
        PluginUI = UI;
        Configuration = configuration;
        mpLibraryZoneDragAndDropData = pLibraryZoneDragAndDropData;
        mpLibraryPresetDragAndDropData = pLibraryPresetDragAndDropData;

        //	Try to read in the zone sort data.
        try
        {
            var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), mZoneSortDataFileName_v1);
            if (File.Exists(zoneSortDataFilePath))
            {
                var jsonStr = File.ReadAllText(zoneSortDataFilePath);
                var sortData = JsonConvert.DeserializeObject<List<ushort>>(jsonStr);
                if (sortData != null)
                    Configuration.PresetLibrary.SetCustomSortOrder(sortData);
            }
            else
            {
                Plugin.Log.Information("No zone sort order file found; using default sort order.");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Unable to load library zone sort data:\r\n{e}");
        }
    }

    public void Dispose()
    {
        //	Try to save off the zone sort data if we have any.
        WriteZoneSortDataToFile();
    }

    public void Draw()
    {
        //	Handle game window docking stuff.
        var dockedWindowPos = Vector2.Zero;
        var fieldMarkerAddonVisible = false;
        unsafe
        {
            var pFieldMarkerAddon = (AtkUnitBase*) Plugin.GameGui.GetAddonByName("FieldMarker");
            if (pFieldMarkerAddon != null && pFieldMarkerAddon->IsVisible && pFieldMarkerAddon->RootNode != null)
            {
                fieldMarkerAddonVisible = true;
                dockedWindowPos.X = pFieldMarkerAddon->X + pFieldMarkerAddon->RootNode->Width * pFieldMarkerAddon->Scale;
                dockedWindowPos.Y = pFieldMarkerAddon->Y;
            }
        }

        WindowVisible = Configuration.OpenAndCloseWithFieldMarkerAddon switch
        {
            true when FieldMarkerAddonWasOpen && !fieldMarkerAddonVisible => false,
            true when !FieldMarkerAddonWasOpen && fieldMarkerAddonVisible => true,
            _ => WindowVisible
        };

        FieldMarkerAddonWasOpen = fieldMarkerAddonVisible;

        if (!WindowVisible)
            return;

        //	Draw the window.
        if (Configuration.AttachLibraryToFieldMarkerAddon && fieldMarkerAddonVisible)
            ImGui.SetNextWindowPos(dockedWindowPos);

        ImGui.SetNextWindowSize(new Vector2(375, 375) * ImGui.GetIO().FontGlobalScale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(375, 375) * ImGui.GetIO().FontGlobalScale, new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin(Loc.Localize("Window Title: Waymark Library", "Waymark Library") + "###Waymark Library", ref mWindowVisible, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar))
        {
            ImGuiUtils.TitleBarHelpButton(() => { PluginUI.HelpWindow.OpenHelpWindow(HelpWindowPage.General); }, 1, UiBuilder.IconFont);
            DrawWaymarkButtons();

            var previouslyFilteredOnZone = Configuration.FilterOnCurrentZone;
            ImGui.Checkbox(Loc.Localize("Config Option: Filter on Current Zone", "Filter on Current Zone") + "###Filter on Current Zone Checkbox", ref Configuration.mFilterOnCurrentZone);
            if (Configuration.FilterOnCurrentZone != previouslyFilteredOnZone)
                Configuration.Save(); //	I'd rather just save the state when the plugin is unloaded, but that's not been feasible in the past.

            var saveCurrentWaymarksButtonText = Loc.Localize("Button: Save Current Waymarks", "Save Current Waymarks");
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(saveCurrentWaymarksButtonText).X - ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().WindowPadding.X);
            if (ImGui.Button(saveCurrentWaymarksButtonText + "###Save Current Waymarks Button"))
            {
                FieldMarkerPreset currentWaymarks = new();
                if (MemoryHandler.GetCurrentWaymarksAsPresetData(ref currentWaymarks))
                {
                    if (Configuration.PresetLibrary.ImportPreset(currentWaymarks) >= 0)
                        Configuration.Save();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.Button] * 0.5f);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

                ImGui.Button(saveCurrentWaymarksButtonText + "###Save Current Waymarks Button");

                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
            }

            // The string to use for filtering the list of zones
            var zoneFilterString = "";

            // Show the search text box when not filtering on current zone
            if (Configuration.ShowLibraryZoneFilterBox && !Configuration.FilterOnCurrentZone && Configuration.SortPresetsByZone)
            {
                ImGui.PushItemWidth(ImGui.CalcTextSize("_").X * 20u);
                ImGui.InputText(Loc.Localize("Library Window Text: Zone Search Label", "Search Zones") + "###Zone Filter Text Box", ref mSearchText, 16u);
                ImGui.PopItemWidth();

                zoneFilterString = mSearchText;
            }

            ImGui.BeginChild("###Library Preset List Child Window");

            (int, int, bool)? presetDragDropResult = null;
            (ushort, ushort)? zoneDragDropResult = null;
            ImGui.BeginGroup();
            if (Configuration.PresetLibrary.Presets.Count > 0)
            {
                if (Configuration.mSortPresetsByZone)
                {
                    var anyPresetsVisibleWithCurrentFilters = false;
                    var dict = Configuration.PresetLibrary.GetSortedIndices(ZoneSortType.Custom, Configuration.SortZonesDescending);
                    /*if( mConfiguration.SortZonesDescending )
                    {
                        var tempZoneResult = DrawZoneDragDropTopOrBottomPlaceholder( true ); //***** TODO: Not using this for now because having this make the list move down feels pretty bad.
                        zoneDragDropResult ??= tempZoneResult;
                    }*/
                    foreach (var zone in dict)
                    {
                        if (!Configuration.FilterOnCurrentZone || zone.Key == ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(Plugin.ClientState.TerritoryType))
                        {
                            var zoneInfo = ZoneInfoHandler.GetZoneInfoFromContentFinderID(zone.Key);

                            if (IsZoneFilteredBySearch(zoneFilterString, zoneInfo))
                            {
                                anyPresetsVisibleWithCurrentFilters = true;

                                if (ImGui.CollapsingHeader(zoneInfo.DutyName))
                                {
                                    var tempZoneResult = DoZoneDragAndDrop(zoneInfo);
                                    var tempPresetResult = DrawPresetsForZone(zone);
                                    zoneDragDropResult ??= tempZoneResult;
                                    presetDragDropResult ??= tempPresetResult;
                                }
                                else
                                {
                                    var tempZoneResult = DoZoneDragAndDrop(zoneInfo);
                                    zoneDragDropResult ??= tempZoneResult;
                                }
                            }
                        }
                    }

                    if (!Configuration.SortZonesDescending)
                    {
                        var tempZoneResult = DrawZoneDragDropTopOrBottomPlaceholder(false);
                        zoneDragDropResult ??= tempZoneResult;
                    }

                    if (!anyPresetsVisibleWithCurrentFilters)
                        ImGui.Text(Loc.Localize("Main Window Text: No Presets Found", "No presets match the current filter."));
                }
                else
                {
                    if (ImGui.CollapsingHeader(Loc.Localize("Header: Presets", "Presets") + "###Presets"))
                        presetDragDropResult = DrawUncategorizedPresets();
                }
            }
            else
            {
                ImGui.Text(Loc.Localize("Main Window Text: Library Empty", "Preset library empty!"));
            }

            ImGui.EndGroup();

            //ImGuiHelpers.ScaledDummy( 20.0f );	//***** TODO: Replace excess spacings with scaled dummies.
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.CollapsingHeader(Loc.Localize("Header: Import Options", "Import Options") + "###Import Options"))
                DrawImportSection();

            if (ImGui.CollapsingHeader(Loc.Localize("Header: Export and Backup Options", "Export/Backup Options") + "###Export/Backup Options"))
                DrawExportSection();

            ImGui.EndChild();

            //	Handle moving a zone header if the user wanted to.
            if (zoneDragDropResult != null)
            {
                //	If it's the first time someone is dragging and dropping, set the sort order to what's currently visible.
                if (!Configuration.PresetLibrary.GetCustomSortOrder().Any())
                {
                    List<ushort> baseSortOrder = new();
                    foreach (var zone in Configuration.PresetLibrary.GetSortedIndices(ZoneSortType.Custom))
                        baseSortOrder.Add(zone.Key);
                    Configuration.PresetLibrary.SetCustomSortOrder(baseSortOrder, Configuration.SortZonesDescending);
                    Plugin.Log.Debug("Tried to set up initial zone sort order.");
                }

                //	Modify the sort entry for the drag and drop.
                Configuration.PresetLibrary.AddOrChangeCustomSortEntry(zoneDragDropResult.Value.Item1, zoneDragDropResult.Value.Item2);
                Plugin.Log.Debug($"Tried to move zone id {zoneDragDropResult.Value.Item1} in front of {zoneDragDropResult.Value.Item2}.");
            }

            //	Handle moving a preset now if the user wanted to.
            if (presetDragDropResult != null)
            {
                SelectedPreset = Configuration.PresetLibrary.MovePreset(presetDragDropResult.Value.Item1, presetDragDropResult.Value.Item2, presetDragDropResult.Value.Item3);
                if (SelectedPreset == -1)
                {
                    Plugin.Log.Debug($"Unable to move preset {presetDragDropResult.Value.Item1} to {(presetDragDropResult.Value.Item3 ? "after " : "")}index {presetDragDropResult.Value.Item2}.");
                }
                else
                {
                    Plugin.Log.Debug($"Moved preset {presetDragDropResult.Value.Item1} to index {SelectedPreset}.");
                    Configuration.Save();
                }

                Marshal.WriteInt32(mpLibraryPresetDragAndDropData, -1);
            }
        }

        //	Store the position and size so that we can keep the companion info window in the right place.
        WindowPos = ImGui.GetWindowPos();
        WindowSize = ImGui.GetWindowSize();

        //	We're done.
        ImGui.End();
    }

    private unsafe (int, int, bool)? DrawPresetsForZone(KeyValuePair<ushort, List<int>> zonePresets)
    {
        var doDragAndDropMove = false;
        var indexToMove = -1;
        var indexToMoveTo = -1;
        var moveToAfter = false;
        var indices = zonePresets.Value;
        for (var i = 0; i < indices.Count; ++i)
        {
            if (ImGui.Selectable($"{Configuration.PresetLibrary.Presets[indices[i]].Name}{(Configuration.ShowLibraryIndexInPresetInfo ? " (" + indices[i].ToString() + ")" : "")}###_Preset_{indices[i]}", indices[i] == SelectedPreset, ImGuiSelectableFlags.AllowDoubleClick))
            {
                //	It's probably a bad idea to allow the selection to change when a preset's being edited.
                if (!PluginUI.EditorWindow.EditingPreset)
                {
                    if (Configuration.AllowUnselectPreset && indices[i] == SelectedPreset && !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        SelectedPreset = -1;
                    else
                        SelectedPreset = indices[i];

                    PluginUI.InfoPaneWindow.CancelPendingDelete();
                }

                //	Place preset when its entry in the library window is double clicked.
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    var preset = Configuration.PresetLibrary.Presets[indices[i]].GetAsGamePreset();
                    MemoryHandler.PlacePreset(preset);
                }
            }

            if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowPresetDragAndDropOrdering && ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
            {
                ImGui.SetDragDropPayload($"PresetIdxZ{zonePresets.Key}", mpLibraryPresetDragAndDropData, sizeof(int));
                Marshal.WriteInt32(mpLibraryPresetDragAndDropData, indices[i]);
                ImGui.Text(Loc.Localize("Drag and Drop Preview: Moving Preset", "Moving: ") + $"{Configuration.PresetLibrary.Presets[indices[i]].Name}{(Configuration.ShowLibraryIndexInPresetInfo ? " (" + indices[i].ToString() + ")" : "")}");
                ImGui.EndDragDropSource();
            }

            if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowPresetDragAndDropOrdering && ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload($"PresetIdxZ{zonePresets.Key}", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
                {
                    if (payload.IsDelivery())
                    {
                        indexToMove = Marshal.ReadInt32(payload.Data);
                        indexToMoveTo = indices[i];
                        doDragAndDropMove = true;
                    }
                    else
                    {
                        ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                    }
                }

                ImGui.EndDragDropTarget();
            }
        }

        if (!PluginUI.EditorWindow.EditingPreset &&
            Configuration.AllowPresetDragAndDropOrdering &&
            ImGui.GetDragDropPayload().NativePtr != null &&
            ImGui.GetDragDropPayload().IsDataType($"PresetIdxZ{zonePresets.Key}") &&
            ImGui.GetDragDropPayload().Data != IntPtr.Zero)
        {
            var draggedIndex = Marshal.ReadInt32(mpLibraryPresetDragAndDropData);
            if (draggedIndex >= 0 && draggedIndex < Configuration.PresetLibrary.Presets.Count && Configuration.PresetLibrary.Presets[draggedIndex].MapID == zonePresets.Key)
            {
                ImGui.Selectable(Loc.Localize("Drag and Drop Preview: Move to Bottom", "<Move To Bottom>") + "###<Move To Bottom>");
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload($"PresetIdxZ{zonePresets.Key}", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
                    {
                        if (payload.IsDelivery())
                        {
                            indexToMove = Marshal.ReadInt32(payload.Data);
                            indexToMoveTo = indices.Last();
                            moveToAfter = true;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }

                    ImGui.EndDragDropTarget();
                }
            }
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new(indexToMove, indexToMoveTo, moveToAfter) : null;
    }

    private unsafe (int, int, bool)? DrawUncategorizedPresets()
    {
        var doDragAndDropMove = false;
        var indexToMove = -1;
        var indexToMoveTo = -1;
        var anyPresetsVisibleWithCurrentFilters = false;
        for (var i = 0; i < Configuration.PresetLibrary.Presets.Count; ++i)
        {
            if (!Configuration.FilterOnCurrentZone || Configuration.PresetLibrary.Presets[i].MapID == ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(Plugin.ClientState.TerritoryType))
            {
                anyPresetsVisibleWithCurrentFilters = true;
                if (ImGui.Selectable($"{Configuration.PresetLibrary.Presets[i].Name}{(Configuration.ShowLibraryIndexInPresetInfo ? " (" + i.ToString() + ")" : "")}###_Preset_{i}", i == SelectedPreset, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    //	It's probably a bad idea to allow the selection to change when a preset's being edited.
                    if (!PluginUI.EditorWindow.EditingPreset)
                    {
                        if (Configuration.AllowUnselectPreset && i == SelectedPreset && !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            SelectedPreset = -1;
                        else
                            SelectedPreset = i;

                        PluginUI.InfoPaneWindow.CancelPendingDelete();
                    }

                    //	Place preset when its entry in the library window is double clicked.
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        var preset = Configuration.PresetLibrary.Presets[i].GetAsGamePreset();
                        MemoryHandler.PlacePreset(preset);
                    }
                }

                if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowPresetDragAndDropOrdering && ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
                {
                    ImGui.SetDragDropPayload($"PresetIdxAnyZone", mpLibraryPresetDragAndDropData, sizeof(int));
                    Marshal.WriteInt32(mpLibraryPresetDragAndDropData, i);
                    ImGui.Text(Loc.Localize("Drag and Drop Preview: Moving Preset", "Moving: ") + $"{Configuration.PresetLibrary.Presets[i].Name}{(Configuration.ShowLibraryIndexInPresetInfo ? " (" + i.ToString() + ")" : "")}");
                    ImGui.EndDragDropSource();
                }

                if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowPresetDragAndDropOrdering && ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload($"PresetIdxAnyZone", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
                    {
                        if (payload.IsDelivery())
                        {
                            indexToMove = Marshal.ReadInt32(payload.Data);
                            indexToMoveTo = i;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }

                    ImGui.EndDragDropTarget();
                }
            }
        }

        if (!PluginUI.EditorWindow.EditingPreset &&
            Configuration.AllowPresetDragAndDropOrdering &&
            ImGui.GetDragDropPayload().NativePtr != null &&
            ImGui.GetDragDropPayload().IsDataType($"PresetIdxAnyZone") &&
            ImGui.GetDragDropPayload().Data != IntPtr.Zero)
        {
            var draggedIndex = Marshal.ReadInt32(mpLibraryPresetDragAndDropData);
            if (draggedIndex >= 0 && draggedIndex < Configuration.PresetLibrary.Presets.Count)
            {
                ImGui.Selectable(Loc.Localize("Drag and Drop Preview: Move to Bottom", "<Move To Bottom>") + "###<Move To Bottom>");
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload($"PresetIdxAnyZone", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
                    {
                        if (payload.IsDelivery())
                        {
                            indexToMove = Marshal.ReadInt32(payload.Data);
                            indexToMoveTo = Configuration.PresetLibrary.Presets.Count;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }

                    ImGui.EndDragDropTarget();
                }
            }
        }

        if (!anyPresetsVisibleWithCurrentFilters)
            ImGui.Text(Loc.Localize("Main Window Text: No Presets Found", "No presets match the current filter."));

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<int, int, bool>(indexToMove, indexToMoveTo, false) : null;
    }

    private unsafe (ushort, ushort)? DoZoneDragAndDrop(ZoneInfo zoneInfo)
    {
        var doDragAndDropMove = false;
        ushort zoneIndexToMove = 0;
        ushort zoneIndexToMoveTo = 0;
        if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowZoneDragAndDropOrdering && ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
        {
            ImGui.SetDragDropPayload($"PresetZoneHeader", mpLibraryZoneDragAndDropData, sizeof(ushort));
            *(ushort*)mpLibraryZoneDragAndDropData = zoneInfo.ContentFinderConditionID;
            ImGui.Text(Loc.Localize("Drag and Drop Preview: Moving Zone", "Moving: ") + $"{zoneInfo.DutyName}");
            ImGui.EndDragDropSource();
        }

        if (!PluginUI.EditorWindow.EditingPreset && Configuration.AllowZoneDragAndDropOrdering && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload($"PresetZoneHeader", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
            if (payload.NativePtr != null && payload.Data != nint.Zero)
            {
                if (payload.IsDelivery())
                {
                    zoneIndexToMove = *(ushort*)payload.Data;
                    zoneIndexToMoveTo = zoneInfo.ContentFinderConditionID;
                    doDragAndDropMove = true;
                }
                else
                {
                    if (Configuration.SortZonesDescending)
                        ImGuiUtils.AddUnderline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                    else
                        ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                }
            }

            ImGui.EndDragDropTarget();
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<ushort, ushort>(zoneIndexToMove, zoneIndexToMoveTo) : null;
    }

    private unsafe (ushort, ushort)? DrawZoneDragDropTopOrBottomPlaceholder(bool isTop)
    {
        var doDragAndDropMove = false;
        ushort zoneIndexToMove = 0;
        ushort zoneIndexToMoveTo = 0;
        if (!PluginUI.EditorWindow.EditingPreset &&
            Configuration.AllowZoneDragAndDropOrdering &&
            ImGui.GetDragDropPayload().NativePtr != null &&
            ImGui.GetDragDropPayload().IsDataType($"PresetZoneHeader") &&
            ImGui.GetDragDropPayload().Data != IntPtr.Zero)
        {
            var draggedZone = *(ushort*)mpLibraryZoneDragAndDropData;
            if (draggedZone >= 0)
            {
                if (isTop)
                    ImGui.CollapsingHeader(Loc.Localize("Drag and Drop Preview: Move to Top", "<Move To Top>") + "###<Move To Top>");
                else
                    ImGui.CollapsingHeader(Loc.Localize("Drag and Drop Preview: Move to Bottom", "<Move To Bottom>") + "###<Move To Bottom>");

                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload($"PresetZoneHeader", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero)
                    {
                        if (payload.IsDelivery())
                        {
                            zoneIndexToMove = draggedZone;
                            zoneIndexToMoveTo = ushort.MaxValue;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            if (isTop)
                                ImGuiUtils.AddUnderline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                            else
                                ImGuiUtils.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }

                    ImGui.EndDragDropTarget();
                }
            }
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<ushort, ushort>(zoneIndexToMove, zoneIndexToMoveTo) : null;
    }

    private void DrawImportSection()
    {
        ImGui.BeginGroup(); //Buttons don't seem to work under a header without being in a group.
        ImGui.InputTextWithHint("##JSONImportTextBox", Loc.Localize("Text Box Prompt: Import", "Paste a preset here and click \"Import\"."), ref mPresetImportString, 1024); //Most exports max out around 500 characters with all waymarks, so this leaves heaps of room for a long name.
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("Button: Import", "Import") + "###Import Button"))
        {
            Plugin.Log.Information($"Attempting to import preset string: \"{mPresetImportString}\"");
            if (Configuration.PresetLibrary.ImportPreset(PresetImportString) >= 0)
            {
                PresetImportString = "";
                Configuration.Save();
            }
        }

        if (ImGui.Button(Loc.Localize("Main Window Text: Import from Game Slot Label", "Or import from game slot: ")))
        {
            if (Configuration.PresetLibrary.ImportPreset(MemoryHandler.ReadSlot(mGameSlotDropdownSelection)) >= 0)
                Configuration.Save();
        }

        ImGui.SameLine();
        var comboWidth = ImGui.CalcTextSize($"{MemoryHandler.MaxPresetSlotNum}").X + ImGui.GetStyle().FramePadding.X * 3f + ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetNextItemWidth(comboWidth);
        if (ImGui.BeginCombo("###ImportGameSlotNumberDropdown", $"{mGameSlotDropdownSelection}"))
        {
            for (uint i = 1; i <= MemoryHandler.MaxPresetSlotNum; ++i)
                if (ImGui.Selectable($"{i}")) mGameSlotDropdownSelection = i;

            ImGui.EndCombo();
        }
        ImGui.InputTextWithHint("###RemoteURLInput", "Paste a URL here and click Get", ref mUrlImportString, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Add from URL"))
        {
            if (subTask is null)
            {
                try
                {
                    subTask = Configuration.SubscriptionManager.Subscribe(mUrlImportString);
                }
                catch(Exception ex)
                {
                    Plugin.Log.Error(ex.Message);
                    subEx = ex;
                }
            }
        }

        if (subTask != null)
        {
            if (!subTask.IsCompleted)
            {
                ImGui.SameLine();
                ImGui.Text("Loading URL...");
            }
            else
            {
                subTask = null;
                subEx = null;
            }
        }

        switch (subEx)
        {
            case null: break;
            case HttpRequestException: ImGui.SameLine();ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), Loc.Localize("ErrorReqFailed", "Request Failed")); break;
            case TimeoutException: ImGui.SameLine();ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), Loc.Localize("ErrorTimeOut", "Timed out")); break;
            case ArgumentException: ImGui.SameLine();ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), Loc.Localize("ErrorArgument", subEx.Message)); break;
            case DuplicateNameException: ImGui.SameLine();ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), Loc.Localize("ErrorDuplicateName", subEx.Message)); break;
        }
        foreach(var item in Configuration.Subscriptions)
        {
            ImGui.Text(item.RepoUrl + ":" + (item.HasUpdates ? "Has updates" : "No updates") );
            ImGui.SameLine();
            if(ImGui.Button("Check for updates"))
            {
                //Plugin.SubscriptionManager.ScheduleCheckForUpdates(item);
            }
            ImGui.SameLine();
            if (!item.HasUpdates)
            {
                ImGui.Text("Up to date");
            }
            else
            {
                if (ImGui.Button("Update"))
                {
                    //Plugin.SubscriptionManager.Sync(item);
                }
            }
            ImGui.SameLine();
            ImGui.Text(item.LastUpdateCheck.ToShortDateString() +" at "+ item.LastUpdateCheck.ToShortTimeString());

        }
        try
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uF0C1");
            ImGui.PopFont();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGuiUtils.URLLink("https://github.com/PunishedPineapple/WaymarkPresetPlugin/wiki/Preset-Resources", Loc.Localize("Main Window Text: Preset Resources Link", "Where to find importable presets"), false, UiBuilder.IconFont);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Unable to open the requested link:\r\n{e}");
        }

        ImGui.EndGroup();
    }

    private void DrawExportSection()
    {
        ImGui.BeginGroup(); //Buttons don't seem to work under a header without being in a group.
        if (ImGui.Button(Loc.Localize("Button: Export All Presets to Clipboard", "Export All Presets to Clipboard") + "###Export All Presets to Clipboard Button"))
        {
            try
            {
                var str = Configuration.PresetLibrary.Presets.Aggregate("", (current, preset) => current + (WaymarkPresetExport.GetExportString(preset) + "\r\n"));
                ImGui.SetClipboardText(str);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Error while exporting all presets: {e}");
            }
        }

        if (ImGui.Button(Loc.Localize("Button: Backup Current Config", "Backup Current Config") + "###Backup Current Config Button"))
        {
            Configuration.Save();
            WriteZoneSortDataToFile();
            PluginUI.MapWindow.WriteMapViewStateToFile();
            Configuration.BackupConfigFile();
            Configuration.BackupConfigFolderFile(mZoneSortDataFileName_v1[..mZoneSortDataFileName_v1.LastIndexOf('.')], mZoneSortDataFileName_v1[(mZoneSortDataFileName_v1.LastIndexOf('.') + 1)..]);
            Configuration.BackupConfigFolderFile(WindowMap.mMapViewStateDataFileName_v1[..WindowMap.mMapViewStateDataFileName_v1.LastIndexOf('.')], WindowMap.mMapViewStateDataFileName_v1[(WindowMap.mMapViewStateDataFileName_v1.LastIndexOf('.') + 1)..]);
        }

        ImGuiUtils.HelpMarker(Loc.Localize("Help: Backup Current Config", "Copies the current config file to a backup folder in the Dalamud \"pluginConfigs\" directory."));
        ImGui.EndGroup();
    }

    private void DrawWaymarkButtons()
    {
        //***** TODO: Move to a separate window on the side probably if we every actually do this.
        /*if( ImGui.Button( "A" ) )
        {
            mCommandManager.ProcessCommand( "/waymark a" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "B" ) )
        {
            mCommandManager.ProcessCommand( "/waymark b" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "C" ) )
        {
            mCommandManager.ProcessCommand( "/waymark c" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "D" ) )
        {
            mCommandManager.ProcessCommand( "/waymark d" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "1" ) )
        {
            mCommandManager.ProcessCommand( "/waymark 1" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "2" ) )
        {
            mCommandManager.ProcessCommand( "/waymark 2" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "3" ) )
        {
            mCommandManager.ProcessCommand( "/waymark 3" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "4" ) )
        {
            mCommandManager.ProcessCommand( "/waymark 4" );
        }
        ImGui.SameLine();
        if( ImGui.Button( "Clear" ) )
        {
            mCommandManager.ProcessCommand( "/waymarks clear" );
        }*/
    }

    internal void WriteZoneSortDataToFile()
    {
        try
        {
            var sortData = Configuration.PresetLibrary.GetCustomSortOrder();
            if (sortData.Any())
            {
                var jsonStr = JsonConvert.SerializeObject(sortData);
                var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), mZoneSortDataFileName_v1);
                File.WriteAllText(zoneSortDataFilePath, jsonStr);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Unable to save library zone sort data:\r\n{e}");
        }
    }

    internal void ClearAllZoneSortData()
    {
        Configuration.PresetLibrary.ClearCustomSortOrder();
        var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), mZoneSortDataFileName_v1);
        if (File.Exists(zoneSortDataFilePath))
            File.Delete(zoneSortDataFilePath);
    }

    public void TrySetSelectedPreset(int presetIndex)
    {
        if (!PluginUI.EditorWindow.EditingPreset)
            SelectedPreset = presetIndex;
    }

    public void TryDeselectPreset()
    {
        if (!PluginUI.EditorWindow.EditingPreset)
            SelectedPreset = -1;
    }

    private bool IsZoneFilteredBySearch(string zoneFilterString, ZoneInfo zoneInfo)
    {
        var matchingZones = LibraryWindowZoneSearcher.GetMatchingZones(zoneFilterString);
        return zoneFilterString.Length == 0 || matchingZones.Any(id => id == zoneInfo.ContentFinderConditionID);
    }
}