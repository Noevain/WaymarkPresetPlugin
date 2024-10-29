using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CheapLoc;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ImGuiNET;
using Newtonsoft.Json;
using WaymarkPresetPlugin.Subscription;

namespace WaymarkPresetPlugin;

public class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static ITextureProvider Texture { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;

    internal const string InternalName = "WaymarkPresetPlugin";

    internal static string TextCommandName => "/pwaymark";
    internal static string SubcommandConfig => "config";
    internal static string SubcommandSlotInfo => "slotinfo";
    internal static string SubcommandPlace => "place";
    internal static string SubcommandImport => "import";
    internal static string SubcommandExport => "export";
    internal static string SubcommandExportAll => "exportall";
    internal static string SubcommandHelp => "help";
    internal static string SubCommandHelpCommands => "commands";
    internal static string SubCommandArgExportIncludeTime => "-t";
    internal static string SubCommandArgExportIsGameSlot => "-g";

    public ushort CurrentTerritoryTypeID { get; protected set; }
    protected Configuration Configuration;
    protected PluginUI PluginUI;

    public Plugin()
    {
        //	Localization and Command Initialization
        OnLanguageChanged(PluginInterface.UiLanguage);

        //	Configuration
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ZoneInfoHandler.Init();
        //	UI Initialization
        PluginUI = new PluginUI(Configuration);

        //	Event Subscription
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        PluginInterface.LanguageChanged += OnLanguageChanged;
        ClientState.TerritoryChanged += OnTerritoryChanged;

        //	IPC
        IpcProvider.RegisterIPC(this);
    }

    //	Cleanup
    public void Dispose()
    {
        IpcProvider.UnregisterIPC();
        Commands.RemoveHandler(TextCommandName);

        PluginInterface.LanguageChanged -= OnLanguageChanged;
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;

        PluginUI?.Dispose();
    }

    protected void OnLanguageChanged(string langCode)
    {
        var allowedLang = new List<string> { "fr", };

        Log.Information("Trying to set up Loc for culture {0}", langCode);

        if (allowedLang.Contains(langCode))
        {
            Loc.Setup(File.ReadAllText(Path.Join(
                Path.Join(PluginInterface.AssemblyLocation.DirectoryName, "Resources\\Localization\\"),
                $"loc_{langCode}.json")));
        }
        else
        {
            Loc.SetupWithFallbacks();
        }

        //	Set up the command handler with the current language.
        if (Commands.Commands.ContainsKey(TextCommandName))
        {
            Commands.RemoveHandler(TextCommandName);
        }

        Commands.AddHandler(TextCommandName, new CommandInfo(ProcessTextCommand)
        {
            HelpMessage = Loc.Localize("Text Command Description", "Performs waymark preset commands.  Use \"{0}\" for detailed usage information.").Format($"{TextCommandName} {SubcommandHelp}")
        });
    }

    //	Text Commands
    protected void ProcessTextCommand(string command, string args)
    {
        var subCommand = "";
        var subCommandArgs = "";
        var argsArray = args.Split(' ');
        if (argsArray.Length > 0)
            subCommand = argsArray[0];

        if (argsArray.Length > 1)
        {
            //	Recombine because there might be spaces in JSON or something, that would make splitting it bad.
            for (var i = 1; i < argsArray.Length; ++i)
                subCommandArgs += argsArray[i] + ' ';

            subCommandArgs = subCommandArgs.Trim();
        }

        //	Process the commands.
        var suppressResponse = Configuration.SuppressCommandLineResponses;
        var commandResponse = "";
        subCommand = subCommand.ToLower();
        if (subCommand.Length == 0)
        {
            PluginUI.LibraryWindow.WindowVisible = !PluginUI.LibraryWindow.WindowVisible;
        }
        else if (subCommand == SubcommandConfig)
        {
            PluginUI.SettingsWindow.WindowVisible = !PluginUI.SettingsWindow.WindowVisible;
        }
        else if (subCommand == "debug")
        {
            PluginUI.DebugWindow.WindowVisible = !PluginUI.DebugWindow.WindowVisible;
        }
        else if (subCommand == SubcommandSlotInfo)
        {
            commandResponse = ProcessTextCommand_SlotInfo(subCommandArgs);
        }
        else if (subCommand == SubcommandPlace)
        {
            commandResponse = ProcessTextCommand_Place(subCommandArgs);
        }
        else if (subCommand == SubcommandImport)
        {
            commandResponse = ProcessTextCommand_Import(subCommandArgs);
        }
        else if (subCommand == SubcommandExport)
        {
            commandResponse = ProcessTextCommand_Export(subCommandArgs);
        }
        else if (subCommand == SubcommandExportAll)
        {
            commandResponse = ProcessTextCommand_ExportAll(subCommandArgs);
        }
        else if (subCommand == SubcommandHelp || subCommand == "?")
        {
            commandResponse = ProcessTextCommand_Help(subCommandArgs);
            suppressResponse = false;
        }
        else
        {
            commandResponse = ProcessTextCommand_Help(subCommandArgs);
        }

        //	Send any feedback to the user.
        if (commandResponse.Length > 0 && !suppressResponse)
            ChatGui.Print(commandResponse);
    }

    protected string ProcessTextCommand_Help(string args)
    {
        args = args.ToLower();
        if (args == SubCommandHelpCommands)
        {
            return Loc.Localize("Text Command Response: Help - Subcommands", "Valid commands are as follows: {0}, {1}, {2}, {3}, {4}, and {5}.  If no command is provided, the preset library will be opened.  Type \"{6} <command>\" for detailed subcommand information.")
                    .Format(SubcommandPlace, SubcommandImport, SubcommandExport, SubcommandExportAll, SubcommandSlotInfo, SubcommandConfig, $"{TextCommandName} {SubcommandHelp}");
        }

        if (args == SubcommandConfig)
        {
            return Loc.Localize("Text Command Response: Help - Config", "Opens the settings window.");
        }

        if (args == SubcommandSlotInfo)
        {
            return Loc.Localize("Text Command Response: Help - Slot Info", "Prints the data saved in the game's slots to the chat window.  Usage: \"{0} <slot>\".  The slot number can be any valid game slot.")
                .Format($"{TextCommandName} {SubcommandSlotInfo}");
        }

        if (args == SubcommandPlace)
        {
            return Loc.Localize("Text Command Response: Help - Place", "Places the preset with the specified name (if possible).  Quotes MUST be used around the name.  May also specify preset index without quotes instead.  Usage: \"{0} <name>|<index>\".  Name must match exactly (besides case).  Index can be any valid libary preset number.")
                .Format($"{TextCommandName} {SubcommandPlace}");
        }

        if (args == SubcommandImport)
        {
            return Loc.Localize("Text Command Response: Help - Import", "Copies one of the game's five preset slots to the library.  Usage: \"{0} <slot>\".  The slot number can be any valid game slot.  Command-line import of a formatted preset string is not supported due to length restrictions in the game's chat box.")
                .Format($"{TextCommandName} {SubcommandImport}");
        }

        if (args == SubcommandExport)
        {
            return Loc.Localize("Text Command Response: Help - Export", "Copies a preset from the library to the specified game slot *or* copies a preset to the clipboard, depending on flags and parameters.  Usage: \"{0} [{1}] [{2}] <slot|index> [slot]\".  The slot number can be any valid game slot, and index can be any valid library preset number.  Use of the {3} flag specifies that the first number is a game slot, not a library index.  Use of the {4} flag includes the last-modified time in the clipboard export.")
                .Format($"{TextCommandName} {SubcommandExport}", SubCommandArgExportIncludeTime, SubCommandArgExportIsGameSlot, SubCommandArgExportIsGameSlot, SubCommandArgExportIncludeTime);
        }

        if (args == SubcommandExportAll)
        {
            return Loc.Localize("Text Command Response: Help - Export All", "Copies all presets in the library to the clipboard, one per line.  Add {0} if you wish to include the last-modified timestamp in the export.")
                .Format(SubCommandArgExportIncludeTime);
        }

        return Loc.Localize("Text Command Response: Help", "Use \"{0}\" to open the GUI.  Use \"{1}\" for a list of text commands.")
            .Format(TextCommandName, $"{TextCommandName} {SubcommandHelp} {SubCommandHelpCommands}");
    }

    protected string ProcessTextCommand_SlotInfo(string args)
    {
        if (args.Length == 1 && uint.TryParse(args, out var gameSlotToCopy) && gameSlotToCopy >= 1 && gameSlotToCopy <= MemoryHandler.MaxPresetSlotNum)
        {
            try
            {
                var tempPreset = WaymarkPreset.Parse(MemoryHandler.ReadSlot(gameSlotToCopy));
                return Loc.Localize("Text Command Response: Slot Info - Success 1", "Slot {0} Contents:\r\n{1}")
                        .Format(gameSlotToCopy, tempPreset.GetPresetDataString(Configuration.GetZoneNameDelegate, Configuration.ShowIDNumberNextToZoneNames));
            }
            catch (Exception e)
            {
                Log.Error($"An unknown error occured while trying to read the game's waymark data:\r\n{e}");
                return Loc.Localize("Text Command Response: Slot Info - Error 1", "An unknown error occured while trying to read the game's waymark data.");
            }
        }

        return Loc.Localize("Text Command Response: Slot Info - Error 3", "An invalid game slot number was provided.");
    }

    protected string ProcessTextCommand_Place(string args)
    {
        //	The index we will want to try to place once we find it.
        int libraryIndex;

        //	If argument is in quotes, search for the preset by name.
        //	Otherwise, search by index.
        if (args.Trim().First() == '"' && args.Trim().Last() == '"')
        {
            var presetName = args.Trim()[1..^1];
            libraryIndex = Configuration.PresetLibrary.Presets.FindIndex((p) => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (libraryIndex < 0 || libraryIndex >= Configuration.PresetLibrary.Presets.Count)
                return Loc.Localize("Text Command Response: Place - Error 1", "Unable to find preset \"{0}\".").Format(presetName);
        }
        else if (!int.TryParse(args.Trim(), out libraryIndex))
        {
            return Loc.Localize("Text Command Response: Place - Error 2", "Invalid preset number \"{0}\".").Format(args);
        }

        //	Try to do the actual placement.
        if (libraryIndex >= 0 && libraryIndex < Configuration.PresetLibrary.Presets.Count)
            return InternalCommand_PlacePresetByIndex(libraryIndex, false) ? "" : Loc.Localize("Text Command Response: Place - Error 3", "An unknown error occured placing preset {0}.").Format(libraryIndex);

        return Loc.Localize("Text Command Response: Place - Error 4", "Invalid preset number \"{0}\".").Format(libraryIndex);

    }

    protected string ProcessTextCommand_Import(string args)
    {
        if (args.Length != 1 || !uint.TryParse(args, out var gameSlotToCopy) || gameSlotToCopy < 1 || gameSlotToCopy > MemoryHandler.MaxPresetSlotNum)
            return Loc.Localize("Text Command Response: Import - Error 3", "An invalid game slot number was provided: \"{0}\".").Format(args);

        try
        {
            var tempPreset = WaymarkPreset.Parse(MemoryHandler.ReadSlot(gameSlotToCopy));
            var importedIndex = Configuration.PresetLibrary.ImportPreset(tempPreset);
            Configuration.Save();

            return Loc.Localize("Text Command Response: Import - Success 1", "Imported game preset {0} as library preset {1}.").Format(gameSlotToCopy, importedIndex);
        }
        catch (Exception e)
        {
            Log.Error($"An unknown error occured while trying to read the game's waymark data:\r\n{e}");
            return Loc.Localize("Text Command Response: Import - Error 1", "An unknown error occured while trying to read the game's waymark data.");
        }
    }

    protected string ProcessTextCommand_Export(string args)
    {
        var parameters = args.Split();
        var includeTimestamp = parameters.Contains(SubCommandArgExportIncludeTime);
        var useGameSlot = parameters.Contains(SubCommandArgExportIsGameSlot);
        var slotIndexNumbers = parameters.Where(x => int.TryParse(x, out _)).ToList();
        WaymarkPreset presetToExport = null;

        try
        {
            if (slotIndexNumbers.Count < 1)
                return Loc.Localize("Text Command Response: Export - Error 1", "No slot or index numbers were provided.");

            if (slotIndexNumbers.Count == 1)
            {
                var indexToExport = int.Parse(slotIndexNumbers[0]);
                if (useGameSlot)
                {
                    if (indexToExport >= 1 && indexToExport <= MemoryHandler.MaxPresetSlotNum)
                        presetToExport = WaymarkPreset.Parse(MemoryHandler.ReadSlot((uint)indexToExport));
                    else
                        return Loc.Localize("Text Command Response: Export - Error 2", "An invalid game slot number ({0}) was provided.").Format(indexToExport);
                }
                else
                {
                    if (indexToExport >= 0 && indexToExport < Configuration.PresetLibrary.Presets.Count)
                        presetToExport = Configuration.PresetLibrary.Presets[indexToExport];
                    else
                        return Loc.Localize("Text Command Response: Export - Error 3", "An invalid library index ({0}) was provided.").Format(indexToExport);
                }

                var exportStr = includeTimestamp ? JsonConvert.SerializeObject(presetToExport) : WaymarkPresetExport.GetExportString(presetToExport);
                ImGui.SetClipboardText(exportStr);

                return Loc.Localize("Text Command Response: Export - Success 1", "Copied to clipboard.");
            }
            else
            {
                var indexToExport = int.Parse(slotIndexNumbers[0]);
                var exportTargetIndex = int.Parse(slotIndexNumbers[1]);
                if (useGameSlot)
                {
                    if (indexToExport >= 1 && indexToExport <= MemoryHandler.MaxPresetSlotNum)
                        presetToExport = WaymarkPreset.Parse(MemoryHandler.ReadSlot((uint)indexToExport));
                    else
                        return Loc.Localize("Text Command Response: Export - Error 4", "An invalid game slot number to export ({0}) was provided.").Format(indexToExport);
                }
                else
                {
                    if (indexToExport >= 0 && indexToExport < Configuration.PresetLibrary.Presets.Count)
                        presetToExport = Configuration.PresetLibrary.Presets[indexToExport];
                    else
                        return Loc.Localize("Text Command Response: Export - Error 5", "An invalid library index ({0}) was provided.").Format(indexToExport);
                }

                if (exportTargetIndex >= 1 && exportTargetIndex <= MemoryHandler.MaxPresetSlotNum)
                {
                    if (MemoryHandler.WriteSlot(exportTargetIndex, presetToExport.GetAsGamePreset()))
                        return Loc.Localize("Text Command Response: Export - Success 2", "Preset exported to game slot {0}.").Format(exportTargetIndex);

                    return Loc.Localize("Text Command Response: Export - Error 6", "Unable to write to game slot {0}!".Format(exportTargetIndex));
                }

                return Loc.Localize("Text Command Response: Export - Error 7", "An invalid game slot number ({0}) was provided as the target.").Format(exportTargetIndex);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Unknown error occured while export the preset:\r\n{e}");
            return Loc.Localize("Text Command Response: Export - Error 8", "An unknown error occured while trying to export the preset.");
        }
    }

    protected string ProcessTextCommand_ExportAll(string args)
    {
        try
        {
            var str = "";
            str = args.ToLower().Trim() == SubCommandArgExportIncludeTime
                ? Configuration.PresetLibrary.Presets.Aggregate(str, (current, preset) => $"{current}{JsonConvert.SerializeObject(preset)}\r\n")
                : Configuration.PresetLibrary.Presets.Aggregate(str, (current, preset) => $"{current}{WaymarkPresetExport.GetExportString(preset)}\r\n");

            ImGui.SetClipboardText(str);

            return Loc.Localize("Text Command Response: Export All - Success 1", "Waymark library copied to clipboard.");
        }
        catch (Exception e)
        {
            Log.Error($"Unknown error occured while trying to copy presets to clipboard:\r\n{e}");
            return Loc.Localize("Text Command Response: Export All - Error 1", "An unknown error occured while trying to copy presets to clipboard.");
        }
    }

    internal List<int> InternalCommand_GetPresetsForContentFinderCondition(ushort contentFinderCondition)
    {
        List<int> foundPresets = new();
        if (contentFinderCondition == 0)
            return foundPresets;

        for (var i = 0; i < Configuration.PresetLibrary.Presets.Count; ++i)
            if (Configuration.PresetLibrary.Presets[i].MapID == contentFinderCondition)
                foundPresets.Add(i);

        return foundPresets;
    }

    internal List<int> InternalCommand_GetPresetsForTerritoryType(uint territoryType)
    {
        var contentFinderCondition = ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(territoryType);
        return InternalCommand_GetPresetsForContentFinderCondition(contentFinderCondition);
    }

    internal List<int> InternalCommand_GetPresetsForCurrentArea()
    {
        return InternalCommand_GetPresetsForTerritoryType(ClientState.TerritoryType);
    }

    internal bool InternalCommand_PlacePresetByIndex(int index, bool requireZoneMatch = true)
    {
        if (index < 0 || index >= Configuration.PresetLibrary.Presets.Count) return false;
        if (requireZoneMatch && Configuration.PresetLibrary.Presets[index].MapID !=
            ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(ClientState.TerritoryType)) return false;

        try
        {
            MemoryHandler.PlacePreset(Configuration.PresetLibrary.Presets[index].GetAsGamePreset());
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"An unknown error occured while attempting to place preset {index}:\r\n{e}");
            return false;
        }
    }

    internal bool InternalCommand_PlacePresetByName(string name, bool requireZoneMatch = true)
    {
        var libraryIndex = Configuration.PresetLibrary.Presets.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return libraryIndex >= 0 && InternalCommand_PlacePresetByIndex(libraryIndex, requireZoneMatch);
    }

    internal bool InternalCommand_PlacePresetByNameAndContentFinderCondition(string name, ushort contentFinderCondition)
    {
        if (contentFinderCondition == 0)
            return false;

        var libraryIndex = Configuration.PresetLibrary.Presets.FindIndex(p => p.MapID == contentFinderCondition && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return libraryIndex >= 0 && InternalCommand_PlacePresetByIndex(libraryIndex);
    }

    internal bool InternalCommand_PlacePresetByNameAndTerritoryType(string name, uint territoryType)
    {
        var contentFinderCondition = ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(territoryType);
        return InternalCommand_PlacePresetByNameAndContentFinderCondition(name, contentFinderCondition);
    }

    internal string GetLibraryPresetName(int index)
    {
        if (index < 0 || index >= Configuration.PresetLibrary.Presets.Count)
            return null;

        return Configuration.PresetLibrary.Presets[index].Name;
    }

    protected void DrawUI()
    {
        PluginUI.Draw();
    }

    protected void DrawConfigUI()
    {
        PluginUI.SettingsWindow.WindowVisible = true;
    }

    protected void OnTerritoryChanged(ushort ID)
    {
        var prevTerritoryTypeInfo = ZoneInfoHandler.GetZoneInfoFromTerritoryTypeID(CurrentTerritoryTypeID);
        var newTerritoryTypeInfo = ZoneInfoHandler.GetZoneInfoFromTerritoryTypeID(ID);
        CurrentTerritoryTypeID = ID;

        //	Auto-save presets on leaving instance.
        if (Configuration.AutoSavePresetsOnInstanceLeave && ZoneInfoHandler.IsKnownContentFinderID(prevTerritoryTypeInfo.ContentFinderConditionID))
        {
            for (uint i = 1; i <= MemoryHandler.MaxPresetSlotNum; ++i)
            {
                try
                {
                    var preset = WaymarkPreset.Parse(MemoryHandler.ReadSlot(i));
                    if (preset.MapID != prevTerritoryTypeInfo.ContentFinderConditionID || Configuration.PresetLibrary.Presets.Any(x => x.Equals(preset)))
                        continue;

                    preset.Name = $"{prevTerritoryTypeInfo.DutyName} - AutoImported";
                    Configuration.PresetLibrary.ImportPreset(preset);
                }
                catch (Exception e)
                {
                    Log.Error($"Error while attempting to auto-import game slot {i}:\r\n{e}");
                }
            }

            Configuration.Save();
        }

        //	Auto-load presets on entering instance.
        if (Configuration.AutoPopulatePresetsOnEnterInstance && ZoneInfoHandler.IsKnownContentFinderID(newTerritoryTypeInfo.ContentFinderConditionID))
        {
            var presetsToAutoLoad = Configuration.PresetLibrary.Presets
                .Where(x => x.MapID == newTerritoryTypeInfo.ContentFinderConditionID)
                .Take(MemoryHandler.MaxPresetSlotNum).ToList();

            for (var i = 0; i < MemoryHandler.MaxPresetSlotNum; ++i)
            {
                FieldMarkerPreset gamePresetData = new();

                if (i < presetsToAutoLoad.Count)
                {
                    var preset = presetsToAutoLoad[i];
                    gamePresetData = preset.GetAsGamePreset();
                }

                try
                {
                    MemoryHandler.WriteSlot(i + 1, gamePresetData);
                }
                catch (Exception e)
                {
                    Log.Error($"Error while auto copying preset data to game slot {i}:\r\n{e}");
                }
            }
        }
    }
}