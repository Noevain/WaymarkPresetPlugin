using System;
using System.Collections.Generic;
using System.Linq;
using CheapLoc;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Newtonsoft.Json;

namespace WaymarkPresetPlugin;

public sealed class WaymarkPresetLibrary
{
    //	It shouldn't happen, but never let the deserializer overwrite this with null.
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    internal List<WaymarkPreset> Presets { get; private set; } = [];

    private readonly ZoneSortComparerBasic ZoneSortComparerDefault = new();
    private readonly ZoneSortComparerAlphabetical ZoneSortComparerAlphabetical = new();
    private readonly ZoneSortComparerCustomOrder ZoneSortComparerCustom = new();

    //***** TODO: Subscribe/unsubscribe to preset's zone change event on add/remove from library, and update sort stuff based on that.  We really need to make the presets list externally immutable though, because we're just begging for a big issue at this point.
    internal int ImportPreset(WaymarkPreset preset)
    {
        WaymarkPreset importedPreset = new(preset);
        return ImportPreset_Common(importedPreset);
    }

    internal int ImportPreset(FieldMarkerPreset gamePresetData)
    {
        try
        {
            var importedPreset = WaymarkPreset.Parse(gamePresetData);
            importedPreset.Name = Loc.Localize("Default Preset Name (Imported)", "Imported");
            return ImportPreset_Common(importedPreset);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Error in WaymarkPresetLibrary.ImportPreset( GamePreset ):\r\n{e}");
            return -1;
        }
    }

    internal int ImportPreset(string importStr)
    {
        try
        {
            var importedPreset = JsonConvert.DeserializeObject<WaymarkPreset>(importStr);
            if (importedPreset != null)
                return ImportPreset_Common(importedPreset);

            Plugin.Log.Warning(
                $"Error in WaymarkPresetLibrary.ImportPreset( string ): Deserialized input resulted in a null!");
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Error in WaymarkPresetLibrary.ImportPreset( string ):\r\n{e}");
        }

        return -1;
    }
    internal int ImportPreset(string importStr,string importPrefix)
    {
        try
        {
            var importedPreset = JsonConvert.DeserializeObject<WaymarkPreset>(importStr);
            if (importedPreset != null)
            {
                importedPreset.Name = Subscription.SubscriptionManager.MakeName(importedPreset.Name, importPrefix);
                return CreateOrReplacePreset(importedPreset);
            }

            Plugin.Log.Warning(
                $"Error in WaymarkPresetLibrary.ImportPreset( string ): Deserialized input resulted in a null!");
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"Error in WaymarkPresetLibrary.ImportPreset( string ):\r\n{e}");
        }

        return -1;
    }

    private int ImportPreset_Common(WaymarkPreset preset)
    {
        Presets.Add(preset);
        if (ZoneSortComparerCustom.ZoneSortOrder.Count != 0 && !ZoneSortComparerCustom.ZoneSortOrder.Contains(preset.MapID))
            AddOrChangeCustomSortEntry(preset.MapID);

        return Presets.Count - 1;
    }

    internal bool DeletePreset(int index)
    {
        if (index >= 0 && index < Presets.Count)
        {
            var presetZone = Presets[index].MapID;
            Presets.RemoveAt(index);
            if (Presets.All(x => x.MapID != presetZone))
                RemoveCustomSortEntry(presetZone);

            return true;
        }

        return false;
    }

    internal int CreateOrReplacePreset(WaymarkPreset preset)
    {
        int target = Presets.FindIndex(element =>  element.Name == preset.Name && element.MapID == preset.MapID);
        if (target == -1)//not found so create
            return ImportPreset_Common(preset);

        if(DeletePreset(target))
            return ImportPreset_Common(preset);

        throw new Exception("Existing preset found but failed to delete");
    }

    internal int MovePreset(int indexToMove, int newPosition, bool placeAfter)
    {
        if (newPosition == indexToMove)
            return indexToMove;

        if (indexToMove >= 0 && indexToMove < Presets.Count && newPosition >= 0 && newPosition <= Presets.Count - (placeAfter ? 1 : 0))
        {
            var preset = Presets[indexToMove];
            Presets.RemoveAt(indexToMove);
            if (newPosition > indexToMove)
                --newPosition;

            if (placeAfter)
                ++newPosition;

            Presets.Insert(newPosition, preset);
            return newPosition;
        }

        return -1;
    }

    internal void AddOrChangeCustomSortEntry(ushort zone, ushort placeBeforeZone = ushort.MaxValue)
    {
        if (zone == placeBeforeZone)
            return;

        var zoneIndex = ZoneSortComparerCustom.ZoneSortOrder.FindIndex((x) => x == zone);
        var moveToIndex = ZoneSortComparerCustom.ZoneSortOrder.FindIndex((x) => x == placeBeforeZone);

        if (moveToIndex == -1)
        {
            if (zoneIndex != -1)
                ZoneSortComparerCustom.ZoneSortOrder.RemoveAt(zoneIndex);
            ZoneSortComparerCustom.ZoneSortOrder.Add(zone);
        }
        else
        {
            if (zoneIndex != -1)
            {
                ZoneSortComparerCustom.ZoneSortOrder.RemoveAt(zoneIndex);
                if (zoneIndex < moveToIndex)
                    --moveToIndex;
            }

            ZoneSortComparerCustom.ZoneSortOrder.Insert(moveToIndex, zone);
        }
    }

    internal void RemoveCustomSortEntry(ushort zone)
    {
        var zoneIndex = ZoneSortComparerCustom.ZoneSortOrder.FindIndex((x) => x == zone);
        if (zoneIndex != -1)
            ZoneSortComparerCustom.ZoneSortOrder.RemoveAt(zoneIndex);
    }

    internal void ClearCustomSortOrder()
    {
        ZoneSortComparerCustom.ZoneSortOrder.Clear();
    }

    internal void SetCustomSortOrder(List<ushort> order, bool isDescending = false)
    {
        if (isDescending)
            order.Reverse();

        ZoneSortComparerCustom.ZoneSortOrder.Clear();
        ZoneSortComparerCustom.ZoneSortOrder.AddRange(order);
    }

    internal List<ushort> GetCustomSortOrder()
    {
        return [..ZoneSortComparerCustom.ZoneSortOrder];
    }

    internal void SetZoneSortDescending(bool b)
    {
        ZoneSortComparerDefault.SortDescending = b;
        ZoneSortComparerAlphabetical.SortDescending = b;
        ZoneSortComparerCustom.SortDescending = b;
    }

    internal SortedDictionary<ushort, List<int>> GetSortedIndices(ZoneSortType sortType)
    {
        IComparer<ushort> comparer = sortType switch
        {
            ZoneSortType.Basic => ZoneSortComparerDefault,
            ZoneSortType.Alphabetical => ZoneSortComparerAlphabetical,
            ZoneSortType.Custom => ZoneSortComparerCustom,
            _ => ZoneSortComparerDefault,
        };

        SortedDictionary<ushort, List<int>> sortedIndices = new(comparer);

        for (var i = 0; i < Presets.Count; ++i)
        {
            if (!sortedIndices.ContainsKey(Presets[i].MapID))
                sortedIndices.Add(Presets[i].MapID, new List<int>());

            sortedIndices[Presets[i].MapID].Add(i);
        }

        return sortedIndices;
    }

    internal SortedDictionary<ushort, List<int>> GetSortedIndices(ZoneSortType sortType, bool sortDescending)
    {
        SetZoneSortDescending(sortDescending);
        return GetSortedIndices(sortType);
    }
}