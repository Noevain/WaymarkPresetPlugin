using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace WaymarkPresetPlugin;

public static class ZoneInfoHandler
{
    private static readonly Dictionary<ushort, ZoneInfo> ZoneInfoDict = new();
    private static readonly Dictionary<uint, ushort> TerritoryTypeIDToContentFinderIDDict = new();
    private static readonly Dictionary<string, List<MapInfo>> MapInfoDict = new();

    //	This is to hard-code that some zones should be included, even if they don't otherwise meet the criteria.  There are a small handful of
    //	ContentFinderCondition IDs that support waymark presets, but are content link type #3, and don't otherwise distinguish themselves in the
    //	sheets in any way that I have found.  There is a separate function that gets called at runtime that determines whether presets are allowed
    //	based on some flag about the current duty, but it doesn't seem to have something in the sheets that corresponds to it perfectly.
    private static readonly List<ushort> BodgeIncludeContentFinderConditionIDs =
    [
        760, // Delubrum Reginae
        761  // Delubrum Reginae (Savage)
    ];

    public static void Init()
    {
        //	Get the game sheets that we need to populate a zone dictionary.
        var territorySheet = Plugin.Data.GetExcelSheet<TerritoryType>()!;
        var contentFinderSheet = Plugin.Data.GetExcelSheet<ContentFinderCondition>()!;
        var mapSheet = Plugin.Data.GetExcelSheet<Map>()!;

        //	Clean out anything that we had before.
        ZoneInfoDict.Clear();
        TerritoryTypeIDToContentFinderIDDict.Clear();

        //	Populate the zero entries ahead of time since there may be a many to one relationship with some zero IDs.
        ZoneInfoDict[0] = ZoneInfo.Unknown;
        TerritoryTypeIDToContentFinderIDDict[0] = 0;

        //	Get the name for every "MapID" that is an instance zone.  This is spread out over a few different sheets.  The ID number that gets used in the actual preset is the column 10 in
        //	TerritoryType.  The zone name is correlated in PlaceName, and the duty name and ContentLink IDs are in ContentFinderCondition.  We are using the Content link because that's what's
        //	returned by the best (working) function that I have been able to find so far for the current instance zone.  Confusingly, as scope has changed a bit, we want to store the actual
        //	ID of the maps for these zones too.  The best solution (for the time being) seems to be to store a pseudo map name string (the base of the map names for that zone) that can be cross-referenced later.
        foreach (var zone in territorySheet)
        {
            if (ZoneInfoDict.ContainsKey((ushort) zone.ContentFinderCondition.RowId) || (zone.ExclusiveType != 2 && !BodgeIncludeContentFinderConditionIDs.Contains((ushort)zone.ContentFinderCondition.RowId)))
                continue;

            if (!contentFinderSheet.HasRow(zone.ContentFinderCondition.RowId))
                continue;

            var contentRow = contentFinderSheet.GetRow(zone.ContentFinderCondition.RowId);
            if (contentRow.ContentLinkType is <= 0 or >= 3 && !BodgeIncludeContentFinderConditionIDs.Contains((ushort) zone.ContentFinderCondition.RowId))
                continue;

            if (!ZoneInfoDict.ContainsKey((ushort) zone.ContentFinderCondition.RowId))
            {
                var dutyName = Utils.ToStr(contentRow.Name).Trim();
                if (dutyName.Length > 0)
                    dutyName = dutyName.First().ToString().ToUpper() + dutyName[1..];

                ZoneInfoDict.Add(
                    (ushort) zone.ContentFinderCondition.RowId,
                    new ZoneInfo(dutyName, Utils.ToStr(zone.PlaceName.Value!.Name), zone.RowId, zone.Map.Value!.Id.ToString().Split('/')[0], (ushort) zone.ContentFinderCondition.RowId, contentRow.Content.RowId));
            }

            TerritoryTypeIDToContentFinderIDDict.TryAdd(zone.RowId, (ushort) zone.ContentFinderCondition.RowId);
        }

        //	Now get all of the map info for each territory.  We're doing it this way rather than solely taking the map column
        //	from the TerritoryType sheet because it's easier to handle when a territory has multiple maps this way, rather than
        //	testing each map name for something other than a "/00" and then incrementing until we find where the maps stop existing.
        foreach (var map in mapSheet)
        {
            var mapZoneKey = map.Id.ToString().Split('/')[0];

            if (!MapInfoDict.ContainsKey(mapZoneKey))
                MapInfoDict.Add(mapZoneKey, []);

            MapInfoDict[mapZoneKey].Add(new MapInfo(map.Id.ExtractText(), map.SizeFactor, map.OffsetX, map.OffsetY, map.PlaceNameSub.Value.Name.ExtractText()));
        }
    }

    public static bool IsKnownContentFinderID(ushort id)
    {
        return id != 0 && ZoneInfoDict.ContainsKey(id);
    }

    public static ZoneInfo GetZoneInfoFromContentFinderID(ushort id)
    {
        return ZoneInfoDict.TryGetValue(id, out var value) ? value : ZoneInfoDict[0];
    }

    public static ZoneInfo GetZoneInfoFromTerritoryTypeID(uint ID)
    {
        var contentFinderID = GetContentFinderIDFromTerritoryTypeID(ID);
        return ZoneInfoDict.TryGetValue(contentFinderID, out var id) ? id : ZoneInfoDict[0];
    }

    public static ushort GetContentFinderIDFromTerritoryTypeID(uint ID)
    {
        return TerritoryTypeIDToContentFinderIDDict.TryGetValue(ID, out var id) ? id : TerritoryTypeIDToContentFinderIDDict[0];
    }

    public static Dictionary<ushort, ZoneInfo> GetAllZoneInfo()
    {
        return ZoneInfoDict;
    }

    public static MapInfo[] GetMapInfoFromTerritoryTypeID(uint id)
    {
        var mapBaseName = GetZoneInfoFromTerritoryTypeID(id).MapBaseName;
        return MapInfoDict.TryGetValue(mapBaseName, out var value) ? value.ToArray() : [];
    }
}

public struct ZoneInfo
{
    public string ZoneName { get; set; }
    public string DutyName { get; set; }
    public uint TerritoryTypeID { get; set; }
    public string MapBaseName { get; set; }
    public ushort ContentFinderConditionID { get; set; }
    public uint ContentLinkID { get; set; }

    public ZoneInfo(string dutyName, string zoneName, uint territoryTypeID, string mapBaseName, ushort contentFinderConditionID, uint contentLinkID)
    {
        DutyName = dutyName;
        ZoneName = zoneName;
        TerritoryTypeID = territoryTypeID;
        MapBaseName = mapBaseName;
        ContentFinderConditionID = contentFinderConditionID;
        ContentLinkID = contentLinkID;
    }

    public static readonly ZoneInfo Unknown = new("Unknown Duty", "Unknown Zone", 0, "default", 0, 0);
}

public struct MapInfo
{
    public string MapID { get; set; }
    public ushort SizeFactor { get; set; }
    public Vector2 Offset { get; set; }
    public string PlaceNameSub { get; set; }

    public MapInfo(string mapID, ushort sizeFactor, short offsetX, short offsetY, string placeNameSub)
    {
        MapID = mapID;
        SizeFactor = sizeFactor;
        Offset = new Vector2(offsetX, offsetY);
        PlaceNameSub = placeNameSub;
    }

    public static readonly MapInfo Unknown = new("default/00", 100, 0, 0, "");

    public string GetMapFilePath(bool smallMap = false)
    {
        return $"ui/map/{MapID}/{MapID.Replace("/", "")}_{(smallMap ? "s" : "m")}.tex";
    }

    public string GetMapParchmentImageFilePath(bool smallMap = false)
    {
        return $"ui/map/{MapID}/{MapID.Replace("/", "")}m_{(smallMap ? "s" : "m")}.tex";
    }

    public Vector2 GetMapCoordinates(Vector2 pixelCoordinates)
    {
        return (pixelCoordinates - new Vector2(1024f)) / SizeFactor * 100f - Offset;
    }

    public Vector2 GetPixelCoordinates(Vector2 mapCoordinates)
    {
        return (mapCoordinates + Offset) / 100f * SizeFactor + new Vector2(1024f);
    }
}