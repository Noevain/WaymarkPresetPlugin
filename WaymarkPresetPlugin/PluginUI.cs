using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Interface.Textures.TextureWraps;
using WaymarkPresetPlugin.UI;

namespace WaymarkPresetPlugin
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    public sealed class PluginUI : IDisposable
    {
        //	Child Windows
        internal readonly WindowLibrary LibraryWindow;
        internal readonly WindowInfoPane InfoPaneWindow;
        internal readonly WindowMap MapWindow;
        internal readonly WindowHelp HelpWindow;
        internal readonly WindowDebug DebugWindow;
        internal readonly WindowSettings SettingsWindow;
        internal readonly WindowEditor EditorWindow;

        //	The fields below are here because multiple windows might need them.
        internal readonly IDalamudTextureWrap[] WaymarkIconTextures = new IDalamudTextureWrap[8];

        //***** TODO: Make private again after debugging.
        internal readonly IntPtr LibraryZoneDragAndDropData;
        internal readonly IntPtr LibraryPresetDragAndDropData;
        internal readonly IntPtr EditWaymarkDragAndDropData;
        internal readonly IntPtr EditWaymarkCoordDragAndDropData;

        public PluginUI(Plugin plugin,Configuration configuration)
        {
            //	Allocate drag and drop memory.
            LibraryZoneDragAndDropData = Marshal.AllocHGlobal(sizeof(uint));
            LibraryPresetDragAndDropData = Marshal.AllocHGlobal(sizeof(int));
            EditWaymarkDragAndDropData = Marshal.AllocHGlobal(sizeof(int));
            EditWaymarkCoordDragAndDropData = Marshal.AllocHGlobal(Marshal.SizeOf<Vector3>());

            if (LibraryZoneDragAndDropData == nint.Zero || LibraryPresetDragAndDropData == nint.Zero || EditWaymarkDragAndDropData == nint.Zero || EditWaymarkCoordDragAndDropData == nint.Zero)
                throw new Exception("Error in PluginUI constructor: Unable to allocate memory for drag and drop info.");

            //	Zero out the memory for debug purposes (we are using the zone drag and drop as a ushort for now, so keep the other bytes clean).
            Marshal.WriteInt32(LibraryZoneDragAndDropData, 0);
            Marshal.WriteInt32(LibraryPresetDragAndDropData, 0);
            Marshal.WriteInt32(EditWaymarkDragAndDropData, 0);
            Marshal.StructureToPtr(Vector3.Zero, EditWaymarkCoordDragAndDropData, true);

            //	Load waymark icons.
            WaymarkIconTextures[0] ??= Plugin.Texture.GetFromGameIcon(61241).RentAsync().Result; //A
            WaymarkIconTextures[1] ??= Plugin.Texture.GetFromGameIcon(61242).RentAsync().Result; //B
            WaymarkIconTextures[2] ??= Plugin.Texture.GetFromGameIcon(61243).RentAsync().Result; //C
            WaymarkIconTextures[3] ??= Plugin.Texture.GetFromGameIcon(61247).RentAsync().Result; //D
            WaymarkIconTextures[4] ??= Plugin.Texture.GetFromGameIcon(61244).RentAsync().Result; //1
            WaymarkIconTextures[5] ??= Plugin.Texture.GetFromGameIcon(61245).RentAsync().Result; //2
            WaymarkIconTextures[6] ??= Plugin.Texture.GetFromGameIcon(61246).RentAsync().Result; //3
            WaymarkIconTextures[7] ??= Plugin.Texture.GetFromGameIcon(61248).RentAsync().Result; //4

            //	Make child windows.
            LibraryWindow = new(plugin,this, configuration, LibraryZoneDragAndDropData, LibraryPresetDragAndDropData);
            InfoPaneWindow = new(this, configuration);
            MapWindow = new(this, configuration);
            HelpWindow = new(this, configuration, EditWaymarkCoordDragAndDropData);
            DebugWindow = new(this);
            SettingsWindow = new(this, configuration);
            EditorWindow = new(this, configuration, EditWaymarkDragAndDropData);
        }

        public void Dispose()
        {
            //	Clean up child windows.
            LibraryWindow?.Dispose();
            InfoPaneWindow?.Dispose();
            HelpWindow?.Dispose();
            MapWindow?.Dispose();
            DebugWindow?.Dispose();
            SettingsWindow?.Dispose();
            EditorWindow?.Dispose();

            //	Clean up any other textures.
            foreach (var t in WaymarkIconTextures)
                t?.Dispose();

            //	Free the drag and drop data.
            Marshal.FreeHGlobal(LibraryZoneDragAndDropData);
            Marshal.FreeHGlobal(LibraryPresetDragAndDropData);
            Marshal.FreeHGlobal(EditWaymarkDragAndDropData);
            Marshal.FreeHGlobal(EditWaymarkCoordDragAndDropData);
        }

        public void Draw()
        {
            //	Draw the sub-windows.
            LibraryWindow.Draw();
            InfoPaneWindow.Draw();
            MapWindow.Draw();
            EditorWindow.Draw();
            SettingsWindow.Draw();
            HelpWindow.Draw();
            DebugWindow.Draw();
        }
    }
}