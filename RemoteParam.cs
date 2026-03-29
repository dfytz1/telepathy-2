using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Undo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Telepathy
{
  /// <summary>
  /// Shared behavior for Remote Sender / Remote Receiver parameters (Andrew Heumann Telepathy pattern).
  /// </summary>
  public abstract class RemoteParam : Param_GenericObject, IGH_InitCodeAware
  {
    // ── Group / color ────────────────────────────────────────────────────────
    public GH_Palette GroupPalette { get; private set; } = GH_Palette.Blue;

    public void SetGroupPalette(GH_Palette palette)
    {
      if (GroupPalette == palette) return;
      GroupPalette = palette;
      Attributes?.ExpireLayout();
      var doc = OnPingDocument();
      if (doc != null)
        doc.ScheduleSolution(10, TelepathyUtils.ConnectMatchingParams);
    }

    public override bool Write(GH_IWriter writer)
    {
      writer.SetInt32("GroupPalette", (int)GroupPalette);
      return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("GroupPalette"))
        GroupPalette = (GH_Palette)reader.GetInt32("GroupPalette");
      return base.Read(reader);
    }

    // ── Color options exposed to sub-classes and the shortcut ────────────────
    internal static readonly (string Label, GH_Palette Palette)[] GroupColors =
    {
      ("Blue",   GH_Palette.Blue),
      ("Pink",   GH_Palette.Pink),
      ("Brown",  GH_Palette.Brown),
      ("Grey",   GH_Palette.Grey),
      ("White",  GH_Palette.White),
      ("Black",  GH_Palette.Black),
      ("Orange", GH_Palette.Warning),
      ("Normal", GH_Palette.Normal),
    };

    public void SetInitCode(string code)
    {
      if (code == "..")
      {
        var doc = OnPingDocument() ?? Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc != null)
          NickName = TelepathyUtils.GetLastUsedKey(doc);
        return;
      }

      NickName = code;
    }

    public override void AddedToDocument(GH_Document document)
    {
      base.AddedToDocument(document);
      document.ScheduleSolution(5, d => TelepathyUtils.ConnectMatchingParams(d, true));
    }

    public override void CreateAttributes()
    {
      Attributes = new RemoteParamAttributes(this);
    }

    /// <summary>7th stroke band (primary … septenary) in the category ribbon.</summary>
    public override GH_Exposure Exposure => GH_Exposure.septenary;

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
      // ── Keys submenu ───────────────────────────────────────────────────────
      var keysMenu = GH_DocumentObject.Menu_AppendItem(menu, "Keys");
      var menuDoc  = OnPingDocument() ?? Grasshopper.Instances.ActiveCanvas?.Document;
      foreach (var key in TelepathyUtils.GetAllKeys(menuDoc).OrderBy(k => k))
      {
        if (!string.IsNullOrEmpty(key))
        {
          var isCurrent = key == NickName;
          GH_DocumentObject.Menu_AppendItem(keysMenu.DropDown, key, Menu_KeyClicked, true, isCurrent);
        }
      }

      // ── Group color submenu ─────────────────────────────────────────────────
      var groupMenu = GH_DocumentObject.Menu_AppendItem(menu, "Group color");
      foreach (var (label, palette) in GroupColors)
      {
        var item = GH_DocumentObject.Menu_AppendItem(
          groupMenu.DropDown, label, Menu_GroupClicked, true, palette == GroupPalette);
        item.Tag = palette;
      }

      base.AppendAdditionalMenuItems(menu);

      // ── Actions section (shared + type-specific) ───────────────────────────
      GH_DocumentObject.Menu_AppendSeparator(menu);

      var keyLabel = string.IsNullOrEmpty(NickName) ? "(empty)" : $"'{NickName}'";
      GH_DocumentObject.Menu_AppendItem(
        menu,
        $"Remove all {keyLabel} senders/receivers",
        Menu_RemoveKeyClicked,
        true,
        false);

      AppendTypeSpecificMenuItems(menu);
    }

    /// <summary>Overridden by Sender/Receiver to append type-specific actions in the same section.</summary>
    protected virtual void AppendTypeSpecificMenuItems(ToolStripDropDown menu) { }

    private void Menu_KeyClicked(object? sender, EventArgs e)
    {
      if (sender is not ToolStripMenuItem keyItem) return;
      var doc = OnPingDocument();
      if (doc == null) { NickName = keyItem.Text; Attributes.ExpireLayout(); return; }

      var before = CaptureState(new[] { this });
      NickName = keyItem.Text;
      Attributes.ExpireLayout();
      RecordUndo(doc, "Set Telepathy Key", before, CaptureState(new[] { this }));
    }

    private void Menu_GroupClicked(object? sender, EventArgs e)
    {
      if (sender is not ToolStripMenuItem item || item.Tag is not GH_Palette palette) return;
      var doc = OnPingDocument();
      if (doc == null) { SetGroupPalette(palette); return; }

      var targets = new List<RemoteParam> { this };
      targets.AddRange(doc.SelectedObjects().OfType<RemoteParam>().Where(p => p != this));

      var before = CaptureState(targets);
      foreach (var t in targets) t.SetGroupPalette(palette);
      RecordUndo(doc, "Set Telepathy Group Color", before, CaptureState(targets));
    }

    private void Menu_RemoveKeyClicked(object? sender, EventArgs e)
    {
      var doc = OnPingDocument();
      if (doc == null) return;
      var key = NickName;
      if (string.IsNullOrEmpty(key)) return;
      TelepathyUtils.ExplodeKey(doc, key);
    }

    // ── Undo helpers (called from Attributes and TelepathyUtils too) ──────────

    internal struct ParamState
    {
      public Guid    Guid;
      public string  NickName;
      public GH_Palette Palette;
    }

    internal static ParamState[] CaptureState(IEnumerable<RemoteParam> parms) =>
      parms.Select(p => new ParamState
      {
        Guid     = p.InstanceGuid,
        NickName = p.NickName,
        Palette  = p.GroupPalette,
      }).ToArray();

    internal static void RecordUndo(GH_Document doc, string desc, ParamState[] before, ParamState[] after)
    {
      var record = new GH_UndoRecord(desc);
      record.AddAction(new RemoteParamStateUndoAction(desc, before, after));
      doc.UndoUtil.RecordEvent(record);
    }

    private sealed class RemoteParamStateUndoAction : IGH_UndoAction
    {
      private readonly string      _desc;
      private readonly ParamState[] _before;
      private readonly ParamState[] _after;

      public RemoteParamStateUndoAction(string desc, ParamState[] before, ParamState[] after)
      {
        _desc   = desc;
        _before = before;
        _after  = after;
      }

      public string       Description      => _desc;
      public bool         ExpiresSolution  => true;
      public bool         ExpiresDisplay   => false;
      public GH_UndoState State            => (GH_UndoState)0;
      public void         Flush()          { }
      public bool         Write(GH_IWriter writer) => true;
      public bool         Read(GH_IReader reader)  => true;

      public void Undo(GH_Document doc) => Apply(doc, _before);
      public void Redo(GH_Document doc) => Apply(doc, _after);

      private static void Apply(GH_Document doc, ParamState[] states)
      {
        foreach (var state in states)
        {
          var obj = doc.FindObject(state.Guid, false) as RemoteParam;
          if (obj == null) continue;
          // Nested class can access RemoteParam's private setter directly.
          obj.GroupPalette = state.Palette;
          obj.NickName     = state.NickName;
          obj.Attributes?.ExpireLayout();
        }
        doc.ScheduleSolution(20);
      }
    }
  }
}
