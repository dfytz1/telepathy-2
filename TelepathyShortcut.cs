using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Telepathy
{
  /// <summary>
  /// Handles Cmd+T (Mac) / Ctrl+T (Windows) on the GH canvas.
  ///
  /// Behavior when non-RemoteParam objects are selected (telepathify):
  ///   • If the output already feeds a Sender — skip it.
  ///   • If the output has downstream recipients — insert a Sender + Receiver(s), severing direct wires.
  ///   • If the output has no recipients — create a floating Sender + Receiver pair.
  ///
  /// Behavior when RemoteParam(s) are selected (single key/color slot):
  ///   1st press  — selects ALL params in the same group and centers on the first.
  ///   2nd+ press — cycles to the next closest unvisited param, centering each time.
  ///                After the last one it wraps around and restarts from the beginning.
  ///
  /// Slot uniqueness: cycles Blue → Pink → Brown → Black, then appends _01, _02 …
  /// </summary>
  internal static class TelepathyShortcut
  {
    // ── Color cycle for auto-assigned group colors ───────────────────────────────
    private static readonly GH_Palette[] CycleColors =
    {
      GH_Palette.Blue,
      GH_Palette.Pink,
      GH_Palette.Brown,
      GH_Palette.Black,
    };

    // ── Canvas-walk cycle state ──────────────────────────────────────────────────
    // Populated when the user first selects a RemoteParam group; subsequent presses
    // step through the nearest-neighbour list without rebuilding it.
    private static (string Key, GH_Palette Palette)? _cycleSlot;
    private static List<RemoteParam>?                 _cycleList;
    private static int                                _cycleIndex;

    internal static void RegisterOn(GH_Canvas canvas)
    {
      canvas.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
      // Cmd+T on Mac  /  Ctrl+T on Windows — no other modifiers
      if (!e.Control || e.KeyCode != Keys.T || e.Alt || e.Shift)
        return;

      var canvas = Instances.ActiveCanvas;
      var doc    = canvas?.Document;
      if (doc == null) return;

      var selected = doc.SelectedObjects().ToList();
      if (selected.Count == 0) return;

      // ── RemoteParam branch ──────────────────────────────────────────────────────
      var selectedRemote = selected.OfType<RemoteParam>().ToList();
      if (selectedRemote.Count > 0)
      {
        var slots = selectedRemote
          .Select(p => (p.NickName, p.GroupPalette))
          .Distinct()
          .ToList();

        if (slots.Count == 1)
        {
          var slot = (Key: slots[0].NickName, Palette: slots[0].GroupPalette);

          var allInGroup = doc.Objects
            .OfType<RemoteParam>()
            .Where(p => p.NickName == slot.Key && p.GroupPalette == slot.Palette)
            .ToList();

          // Only continue cycling when every group member is already selected
          // (i.e. the user hasn't manually broken the selection since the last shortcut press).
          bool allGroupSelected = allInGroup.Count > 0
            && selectedRemote.Count == allInGroup.Count
            && allInGroup.All(p => p.Attributes.Selected);

          bool inCycle = allGroupSelected
            && _cycleSlot.HasValue
            && _cycleSlot.Value.Key     == slot.Key
            && _cycleSlot.Value.Palette == slot.Palette
            && _cycleList != null
            && _cycleList.Count == allInGroup.Count
            && _cycleList.All(p => doc.FindObject(p.InstanceGuid, false) != null);

          if (inCycle)
          {
            // Advance to the next component in the walk; wrap around at the end.
            _cycleIndex = (_cycleIndex + 1) % _cycleList!.Count;
            CenterOn(canvas!, _cycleList[_cycleIndex]);
          }
          else
          {
            // Fresh press: select all siblings. No centering yet — that begins on the next press.
            foreach (var obj in doc.Objects)
              obj.Attributes.Selected = obj is RemoteParam rp
                && rp.NickName == slot.Key && rp.GroupPalette == slot.Palette;

            _cycleSlot  = slot;
            _cycleList  = BuildNearestNeighborOrder(allInGroup, selectedRemote);
            _cycleIndex = -1; // First Advance() call will move it to 0 (the nearest component).
          }
        }
        else
        {
          // Multiple slots selected — just select all, no cycle.
          ResetCycle();
          var targetSlots = new HashSet<(string, GH_Palette)>(
            slots.Select(s => (s.NickName, s.GroupPalette)));
          foreach (var obj in doc.Objects)
            obj.Attributes.Selected = obj is RemoteParam rp
              && targetSlots.Contains((rp.NickName, rp.GroupPalette));
        }

        canvas!.Refresh();
        e.Handled = e.SuppressKeyPress = true;
        return;
      }

      // ── Telepathify branch (non-RemoteParam selection) ──────────────────────────
      ResetCycle();

      var occupiedSlots = new HashSet<(string, GH_Palette)>(
        doc.Objects.OfType<RemoteParam>()
           .Select(p => (p.NickName, p.GroupPalette)));

      var batchSlots = new HashSet<(string, GH_Palette)>();
      int created = 0;

      foreach (var obj in selected)
      {
        List<IGH_Param> outputs;

        if (obj is GH_Component comp)
          outputs = comp.Params.Output.Cast<IGH_Param>().ToList();
        else if (obj is IGH_Param fp && fp is not RemoteParam)
          outputs = new List<IGH_Param> { fp };
        else
          continue;

        foreach (var output in outputs)
        {
          if (output.Recipients.Any(r => r is Param_RemoteSender))
            continue;

          var baseName = string.IsNullOrWhiteSpace(output.NickName) ? "data" : output.NickName;
          var (key, palette) = UniqueSlot(baseName, occupiedSlots, batchSlots);
          batchSlots.Add((key, palette));

          var outRight = output.Attributes?.Bounds.Right ?? output.Attributes?.Pivot.X ?? 0f;
          var outY     = output.Attributes?.Pivot.Y ?? 0f;

          var newSender = new Param_RemoteSender();
          newSender.CreateAttributes();
          newSender.Attributes.Pivot = new PointF(outRight + 50f, outY);
          newSender.SetGroupPalette(palette);
          doc.AddObject(newSender, false, doc.ObjectCount);
          newSender.AddSource(output);

          var recipients = output.Recipients.Where(r => r is not RemoteParam).ToList();

          if (recipients.Count > 0)
          {
            foreach (var recipient in recipients)
            {
              var recLeft = recipient.Attributes?.Bounds.Left ?? recipient.Attributes?.Pivot.X ?? 0f;
              var recY    = recipient.Attributes?.Pivot.Y ?? 0f;

              output.Recipients.Remove(recipient);
              recipient.Sources.Remove(output);

              var newReceiver = new Param_RemoteReceiver();
              newReceiver.CreateAttributes();
              newReceiver.Attributes.Pivot = new PointF(recLeft - 50f, recY);
              newReceiver.SetGroupPalette(palette);
              doc.AddObject(newReceiver, false, doc.ObjectCount);
              recipient.AddSource(newReceiver);
              newReceiver.NickName = key;
            }
          }
          else
          {
            var newReceiver = new Param_RemoteReceiver();
            newReceiver.CreateAttributes();
            newReceiver.Attributes.Pivot = new PointF(outRight + 200f, outY);
            newReceiver.SetGroupPalette(palette);
            doc.AddObject(newReceiver, false, doc.ObjectCount);
            newReceiver.NickName = key;
          }

          newSender.NickName = key;
          created++;
        }
      }

      if (created > 0)
      {
        doc.ScheduleSolution(10);
        e.Handled = e.SuppressKeyPress = true;
      }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static void ResetCycle()
    {
      _cycleSlot  = null;
      _cycleList  = null;
      _cycleIndex = 0;
    }

    /// <summary>Pan the viewport to center on <paramref name="param"/> (no zoom change).</summary>
    private static void CenterOn(GH_Canvas canvas, RemoteParam param)
    {
      if (param.Attributes == null) return;
      canvas.Viewport.MidPoint = param.Attributes.Pivot;
    }

    /// <summary>
    /// Returns <paramref name="all"/> sorted by a greedy nearest-neighbour walk that
    /// starts from the centroid of <paramref name="startFrom"/>.
    /// </summary>
    private static List<RemoteParam> BuildNearestNeighborOrder(
      List<RemoteParam> all, List<RemoteParam> startFrom)
    {
      if (all.Count == 0) return all;

      var cx = startFrom.Average(p => p.Attributes?.Pivot.X ?? 0f);
      var cy = startFrom.Average(p => p.Attributes?.Pivot.Y ?? 0f);

      var remaining = all.ToList();
      var result    = new List<RemoteParam>(all.Count);

      while (remaining.Count > 0)
      {
        var nearest = remaining
          .OrderBy(p => SquaredDist(p.Attributes?.Pivot ?? PointF.Empty, cx, cy))
          .First();
        result.Add(nearest);
        cx = nearest.Attributes?.Pivot.X ?? cx;
        cy = nearest.Attributes?.Pivot.Y ?? cy;
        remaining.Remove(nearest);
      }

      return result;
    }

    private static float SquaredDist(PointF p, float x, float y)
    {
      var dx = p.X - x;
      var dy = p.Y - y;
      return dx * dx + dy * dy;
    }

    /// <summary>
    /// Finds the next free (key, palette) slot for <paramref name="baseName"/>.
    /// Cycles Blue → Pink → Brown → Black for the bare name first, then repeats
    /// the cycle for baseName_01, baseName_02, … until a free slot is found.
    /// </summary>
    private static (string key, GH_Palette palette) UniqueSlot(
      string baseName,
      HashSet<(string, GH_Palette)> occupied,
      HashSet<(string, GH_Palette)> batch)
    {
      foreach (var color in CycleColors)
      {
        var slot = (baseName, color);
        if (!occupied.Contains(slot) && !batch.Contains(slot))
          return slot;
      }

      for (int i = 1; ; i++)
      {
        var suffixed = $"{baseName}_{i:D2}";
        foreach (var color in CycleColors)
        {
          var slot = (suffixed, color);
          if (!occupied.Contains(slot) && !batch.Contains(slot))
            return slot;
        }
      }
    }
  }
}
