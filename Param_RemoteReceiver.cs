using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Telepathy
{
  public class Param_RemoteReceiver : RemoteParam
  {
    private string nicknameKey = "";

    public Param_RemoteReceiver()
      : base()
    {
      nicknameKey = "";
      NickName = nicknameKey;
      WireDisplay = GH_ParamWireDisplay.hidden;
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
      GH_DocumentObject.Menu_AppendItem(menu, "Create matching Sender", Menu_CreateSenderClicked, true, false);
      GH_DocumentObject.Menu_AppendItem(menu, "Convert parallel wires to Senders", Menu_ConvertParallelToSendersClicked, true, false);
    }

    private void Menu_CreateSenderClicked(object? sender, EventArgs e)
    {
      var doc = OnPingDocument();
      if (doc == null) return;

      var newSender = new Param_RemoteSender();
      doc.AddObject(newSender, true, doc.ObjectCount);
      newSender.NickName = NickName;
      newSender.Attributes.Pivot = new PointF(Attributes.Pivot.X - 150, Attributes.Pivot.Y);
      newSender.Attributes.ExpireLayout();
      doc.ScheduleSolution(10);
    }

    /// <summary>
    /// For every downstream consumer of this receiver that also has other (non-Telepathy)
    /// direct sources, disconnect those sources from the consumer and insert a new Sender
    /// (same key as this receiver) after each source. Telepathy auto-wires new senders
    /// to this receiver, keeping the data flowing through the channel.
    /// </summary>
    private void Menu_ConvertParallelToSendersClicked(object? sender, EventArgs e)
    {
      var doc = OnPingDocument();
      if (doc == null) return;

      var key = NickName;
      if (string.IsNullOrEmpty(key)) return;

      var created = 0;

      foreach (var recipient in Recipients.ToList())
      {
        foreach (var source in recipient.Sources.ToList())
        {
          // Skip this receiver itself and any existing Telepathy params.
          if (source == this) continue;
          if (source is RemoteParam) continue;

          // Place the new sender 50px to the right of the source's right edge.
          var attrs = source.Attributes;
          var pivot = attrs?.Pivot ?? new PointF(0, 0);
          var rightEdge = attrs != null ? attrs.Bounds.Right : pivot.X;
          var senderPivot = new PointF(rightEdge + 50, pivot.Y);

          var newSender = new Param_RemoteSender();
          newSender.CreateAttributes();
          newSender.Attributes.Pivot = senderPivot;

          doc.AddObject(newSender, false, doc.ObjectCount);

          // Connect source → new sender.
          newSender.AddSource(source);

          // Disconnect source from the downstream consumer.
          recipient.Sources.Remove(source);
          source.Recipients.Remove(recipient);

          // Setting the key triggers Telepathy auto-wiring: new sender → this receiver.
          newSender.NickName = key;

          created++;
        }
      }

      if (created > 0)
        doc.ScheduleSolution(10);
    }

    public override string TypeName => "Remote Receiver";

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
      get => "Remote Receiver";
      set => base.Name = value;
    }

    public override Guid ComponentGuid => new Guid("08CDCD26-518A-4FE2-8313-A2DB5DCDF800");

    protected override Bitmap Icon => TelepathyIcons.Receiver;
  }
}
