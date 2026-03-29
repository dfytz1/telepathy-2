using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Telepathy
{
  public class Param_RemoteSender : RemoteParam
  {
    private string nicknameKey = "unset";

    public Param_RemoteSender()
      : base()
    {
      nicknameKey = "unset";
      NickName = nicknameKey;
      Hidden = true;
    }

    public override string NickName
    {
      get => nicknameKey = base.NickName;
      set
      {
        nicknameKey   = value;
        base.NickName = value;

        var doc = OnPingDocument();
        if (doc != null)
          doc.ScheduleSolution(10, TelepathyUtils.ConnectMatchingParams);
      }
    }

    protected override void AppendTypeSpecificMenuItems(ToolStripDropDown menu)
    {
      GH_DocumentObject.Menu_AppendItem(menu, "Create matching Receiver", Menu_CreateReceiverClicked, true, false);
      GH_DocumentObject.Menu_AppendItem(menu, "Convert parallel wires to Receivers", Menu_ConvertParallelToReceiversClicked, true, false);
    }

    private void Menu_CreateReceiverClicked(object? sender, EventArgs e)
    {
      var doc = OnPingDocument();
      if (doc == null) return;

      var newParam = new Param_RemoteReceiver();
      doc.AddObject(newParam, true, doc.ObjectCount);
      newParam.NickName = NickName;
      newParam.Attributes.Pivot = new PointF(Attributes.Pivot.X + 150, Attributes.Pivot.Y);
      newParam.Attributes.ExpireLayout();
      doc.ScheduleSolution(10);
    }

    /// <summary>
    /// For every source this sender shares with other (non-Telepathy) recipients,
    /// disconnect those recipients from the shared source and insert a new Receiver
    /// (same key as this sender) in between. Telepathy auto-wires the new receivers.
    /// </summary>
    private void Menu_ConvertParallelToReceiversClicked(object? sender, EventArgs e)
    {
      var doc = OnPingDocument();
      if (doc == null) return;

      var key = NickName;
      if (string.IsNullOrEmpty(key)) return;

      var created = 0;

      foreach (var source in Sources.ToList())
      {
        foreach (var recipient in source.Recipients.ToList())
        {
          // Skip self and any existing Telepathy params.
          if (recipient == this) continue;
          if (recipient is RemoteParam) continue;

          // Place the new receiver 50px to the left of the recipient's left edge.
          var attrs = recipient.Attributes;
          var pivot = attrs?.Pivot ?? new PointF(0, 0);
          var leftEdge = attrs != null ? attrs.Bounds.Left : pivot.X;
          var receiverPivot = new PointF(leftEdge - 50, pivot.Y);

          var newReceiver = new Param_RemoteReceiver();
          newReceiver.CreateAttributes();
          newReceiver.Attributes.Pivot = receiverPivot;

          doc.AddObject(newReceiver, false, doc.ObjectCount);

          // Disconnect recipient from the shared source.
          recipient.Sources.Remove(source);
          source.Recipients.Remove(recipient);

          // Wire recipient to the new receiver.
          recipient.AddSource(newReceiver);

          // Setting the key triggers Telepathy auto-wiring: this sender → new receiver.
          newReceiver.NickName = key;

          created++;
        }
      }

      if (created > 0)
        doc.ScheduleSolution(10);
    }

    public override string TypeName => "Remote Sender";

    public override string Category
    {
      get => "Params";
      set => base.Category = value;
    }

    public override string SubCategory
    {
      get => "Telepathy";
      set => base.SubCategory = value;
    }

    public override string Name
    {
      get => "Remote Sender";
      set => base.Name = value;
    }

    public override Guid ComponentGuid => new Guid("ADA99447-8A42-4C8E-BAA4-C8EF36A372B6");

    protected override Bitmap Icon => TelepathyIcons.Sender;
  }
}
