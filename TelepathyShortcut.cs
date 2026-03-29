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
  /// For each selected component or floating param:
  ///   • If the output already feeds a Sender — skip it (already telepathised).
  ///   • If the output has downstream recipients — insert a Sender after the
  ///     output and a Receiver before each recipient, severing the direct wires.
  ///   • If the output has no recipients — create a floating Sender + Receiver pair.
  ///
  /// Slot uniqueness: a slot is (key, groupColor). The shortcut cycles through
  /// Blue → Pink → Brown → Black before ever appending _01, _02 … Once _01 is
  /// needed, the same four-color cycle repeats for that suffix, and so on.
  /// </summary>
  internal static class TelepathyShortcut
  {
    // Cycle order for auto-assigned group colors.
    private static readonly GH_Palette[] CycleColors =
    {
      GH_Palette.Blue,
      GH_Palette.Pink,
      GH_Palette.Brown,
      GH_Palette.Black,
    };

    internal static void RegisterOn(GH_Canvas canvas)
    {
      canvas.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
      // Cmd+T on Mac  /  Ctrl+T on Windows — no other modifiers
      if (!e.Control || e.KeyCode != Keys.T || e.Alt || e.Shift)
        return;

      var doc = Instances.ActiveCanvas?.Document;
      if (doc == null) return;

      var selected = doc.SelectedObjects().ToList();
      if (selected.Count == 0) return;

      // ── If any selected object is a RemoteParam: select all same-key/color siblings ──
      var selectedRemote = selected.OfType<RemoteParam>().ToList();
      if (selectedRemote.Count > 0)
      {
        var targetSlots = new HashSet<(string, GH_Palette)>(
          selectedRemote.Select(p => (p.NickName, p.GroupPalette)));

        foreach (var obj in doc.Objects)
          obj.Attributes.Selected = obj is RemoteParam rp
                                    && targetSlots.Contains((rp.NickName, rp.GroupPalette));

        Instances.ActiveCanvas?.Refresh();
        e.Handled          = true;
        e.SuppressKeyPress = true;
        return;
      }

      // All (key, palette) slots already occupied in the document.
      var occupiedSlots = new HashSet<(string, GH_Palette)>(
        doc.Objects.OfType<RemoteParam>()
           .Select(p => (p.NickName, p.GroupPalette)));

      // Slots we're about to use in this batch so we don't collide with ourselves.
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
          // Skip outputs that already feed into a Telepathy Sender.
          if (output.Recipients.Any(r => r is Param_RemoteSender))
            continue;

          var baseName = string.IsNullOrWhiteSpace(output.NickName) ? "data" : output.NickName;
          var (key, palette) = UniqueSlot(baseName, occupiedSlots, batchSlots);
          batchSlots.Add((key, palette));

          var outRight = output.Attributes?.Bounds.Right ?? output.Attributes?.Pivot.X ?? 0f;
          var outY     = output.Attributes?.Pivot.Y ?? 0f;

          // ── Create Sender ──────────────────────────────────────────────────
          var newSender = new Param_RemoteSender();
          newSender.CreateAttributes();
          newSender.Attributes.Pivot = new PointF(outRight + 50f, outY);
          newSender.SetGroupPalette(palette);         // set color before adding
          doc.AddObject(newSender, false, doc.ObjectCount);
          newSender.AddSource(output);

          // ── Create Receiver(s) ─────────────────────────────────────────────
          var recipients = output.Recipients
            .Where(r => r is not RemoteParam)
            .ToList();

          if (recipients.Count > 0)
          {
            foreach (var recipient in recipients)
            {
              var recLeft = recipient.Attributes?.Bounds.Left ?? recipient.Attributes?.Pivot.X ?? 0f;
              var recY    = recipient.Attributes?.Pivot.Y ?? 0f;

              // Sever the direct wire.
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
            // No downstream consumers — create a floating pair.
            var newReceiver = new Param_RemoteReceiver();
            newReceiver.CreateAttributes();
            newReceiver.Attributes.Pivot = new PointF(outRight + 200f, outY);
            newReceiver.SetGroupPalette(palette);
            doc.AddObject(newReceiver, false, doc.ObjectCount);
            newReceiver.NickName = key;
          }

          // Set sender key last — by now all receivers are in the doc.
          newSender.NickName = key;
          created++;
        }
      }

      if (created > 0)
      {
        doc.ScheduleSolution(10);
        e.Handled = true;
        e.SuppressKeyPress = true;
      }
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
      // Try base name with each color first.
      foreach (var color in CycleColors)
      {
        var slot = (baseName, color);
        if (!occupied.Contains(slot) && !batch.Contains(slot))
          return slot;
      }

      // All four colors taken for baseName — try suffixed names.
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
