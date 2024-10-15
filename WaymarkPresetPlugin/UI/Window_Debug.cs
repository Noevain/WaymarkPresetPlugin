using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using CheapLoc;
using ImGuiNET;

namespace WaymarkPresetPlugin.UI;

internal sealed class WindowDebug : IDisposable
{
    private bool mWindowVisible = false;

    public bool WindowVisible
    {
        get { return mWindowVisible; }
        set { mWindowVisible = value; }
    }

    private readonly PluginUI mUI;

    private Configuration mConfiguration;

    public WindowDebug(PluginUI UI,Configuration configuration)
    {
        mUI = UI;
        mConfiguration = configuration;
    }

    public void Dispose() { }

    public unsafe void Draw()
    {
        if (!WindowVisible)
            return;

        //	Draw the window.
        ImGui.SetNextWindowSize(new Vector2(375, 340) * ImGui.GetIO().FontGlobalScale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(375, 340) * ImGui.GetIO().FontGlobalScale, new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin(Loc.Localize("Window Title: Debug Tools", "Debug Tools") + "###Debug Tools", ref mWindowVisible))
        {
            if (ImGui.Button("Export Localizable Strings"))
            {
                var pwd = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(Plugin.PluginInterface.AssemblyLocation.DirectoryName!);
                Loc.ExportLocalizable();
                Directory.SetCurrentDirectory(pwd);
            }

            ImGui.Text("Drag and Drop Data:");
            ImGui.Indent();
            ImGui.Text($"Zone: 0x{mUI.LibraryZoneDragAndDropData:X}");
            ImGui.Text($"Preset: 0x{mUI.LibraryPresetDragAndDropData:X}");
            ImGui.Text($"Waymark: 0x{mUI.EditWaymarkDragAndDropData:X}");
            ImGui.Text($"Coords: 0x{mUI.EditWaymarkCoordDragAndDropData:X}");
            ImGui.Spacing();
            if (ImGui.GetDragDropPayload().NativePtr != null)
            {
                ImGui.Text($"Current Payload: 0x{ImGui.GetDragDropPayload().Data:X}");
                ImGui.Text($"Current Payload Contents: 0x{Marshal.ReadInt32(ImGui.GetDragDropPayload().Data)}");
            }

            ImGui.Unindent();
        }

        ImGui.Text("Subscribed repos");
        foreach(var item in mConfiguration.subscribed_repos)
        {
            ImGui.Text(item);
        }
        ImGui.Text("ETag cache");
        foreach(var item in mConfiguration.urls_to_etags)
        {
            ImGui.Text(item.Key + ":");
            ImGui.SameLine();
            ImGui.Text(item.Value);
        }
        //	We're done.
        ImGui.End();
    }
}