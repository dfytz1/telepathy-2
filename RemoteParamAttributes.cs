using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Parameters;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Telepathy
{
  public class RemoteParamAttributes : GH_FloatingParamAttributes
  {
    // Cached once per process — Font is a GDI handle, never allocate per-frame.
    private static readonly Font ArrowFont = new Font("Arial", 10F);

    private Rectangle _textBounds = Rectangle.Empty;
    private GH_StateTagList? _stateTags;

    // Cached text width — only recomputed when NickName changes in Layout().
    private string _cachedNickName = string.Empty;
    private float  _cachedTextWidth = 50f;

    // Cached type flags — set once in the constructor, never change.
    private readonly bool _isSender;
    private readonly bool _isReceiver;

    public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs e)
    {
      if (_stateTags != null)
      {
        _stateTags.TooltipSetup(point, e);
        if (e.Valid)
          return;
      }

      base.SetupTooltip(point, e);
    }

    protected override void Layout()
    {
      // Recompute width only when the key name has changed.
      if (Owner.NickName != _cachedNickName)
      {
        _cachedNickName  = Owner.NickName;
        _cachedTextWidth = Math.Max(GH_FontServer.MeasureString(_cachedNickName, GH_FontServer.StandardBold).Width + 10, 50);
      }
      float textWidth = _cachedTextWidth;
      var bounds = new RectangleF(Pivot.X - 0.5f * textWidth, Pivot.Y - 10f, textWidth, 20f);
      Bounds = bounds;
      Bounds = GH_Convert.ToRectangle(Bounds);

      _textBounds = GH_Convert.ToRectangle(Bounds);

      _stateTags = Owner.StateTags;
      if (_stateTags.Count == 0)
        _stateTags = null;

      if (_stateTags != null)
      {
        _stateTags.Layout(GH_Convert.ToRectangle(Bounds), GH_StateTagLayoutDirection.Left);
        var tagBox = _stateTags.BoundingBox;
        if (!tagBox.IsEmpty)
        {
          tagBox.Inflate(3, 0);
          Bounds = RectangleF.Union(Bounds, tagBox);
        }
      }

      if (_isSender)
      {
        var arrowRect = new RectangleF(Bounds.Right, Bounds.Bottom, 10, 1);
        Bounds = RectangleF.Union(Bounds, arrowRect);
      }

      if (_isReceiver)
      {
        var arrowRect = new RectangleF(Bounds.Left - 15, Bounds.Bottom, 15, 1);
        Bounds = RectangleF.Union(Bounds, arrowRect);
      }
    }

    public RemoteParamAttributes(Param_GenericObject owner)
      : base(owner)
    {
      _isSender   = owner is Param_RemoteSender;
      _isReceiver = owner is Param_RemoteReceiver;
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
      if (channel == GH_CanvasChannel.Wires)
      {
        // Receivers hide their wires by design — skip entirely.
        // Senders render only their one incoming wire when on screen.
        if (_isSender && Owner.SourceCount > 0)
        {
          var wireBounds = Bounds;
          if (canvas.Viewport.IsVisible(ref wireBounds, 200f))
            base.RenderIncomingWires(canvas.Painter, Owner.Sources, Owner.WireDisplay);
        }
        return;
      }

      if (channel != GH_CanvasChannel.Objects)
        return;

      // Visibility check first — skip all GDI work for off-screen params.
      var bounds = Bounds;
      if (!canvas.Viewport.IsVisible(ref bounds, 10f))
        return;
      Bounds = bounds;

      // ── Full custom render — do NOT call base.Render ──────────────────────────
      // base.Render would create a second capsule on top of ours (pure overdraw),
      // and may iterate Owner.Recipients for decorators (O(n) on the sender).
      var palette = ((RemoteParam)Owner).GroupPalette;
      var hidden  = ((Param_GenericObject)Owner).Hidden;

      using (var capsule = GH_Capsule.CreateTextCapsule(Bounds, _textBounds, palette, Owner.NickName))
      {
        capsule.AddInputGrip(InputGrip.Y);
        capsule.AddOutputGrip(OutputGrip.Y);
        capsule.Render(graphics, Selected, Owner.Locked, hidden);
      }

      _stateTags?.RenderStateTags(graphics);

      PointF arrowLocation = PointF.Empty;
      if (_isReceiver) arrowLocation = new PointF(bounds.Left  + 10, OutputGrip.Y + 2);
      if (_isSender)   arrowLocation = new PointF(bounds.Right - 10, OutputGrip.Y + 2);
      if (arrowLocation != PointF.Empty)
        RenderArrow(graphics, arrowLocation);
    }

    private static void RenderArrow(Graphics graphics, PointF loc)
    {
      GH_GraphicsUtil.RenderCenteredText(graphics, "\u2192", ArrowFont, Color.Black, new PointF(loc.X, loc.Y - 1.5f));
    }

    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      var doc = Owner.OnPingDocument();
      var allKeys = doc != null
        ? TelepathyUtils.GetAllKeys(doc)
        : new System.Collections.Generic.List<string>();

      var newKey = KeyPickerUi.ShowPicker(allKeys, Owner.NickName);
      if (newKey != null && Owner is RemoteParam rp)
      {
        var before = RemoteParam.CaptureState(new[] { rp });
        rp.NickName = newKey;
        rp.Attributes.ExpireLayout();
        if (doc != null)
          RemoteParam.RecordUndo(doc, "Set Telepathy Key", before, RemoteParam.CaptureState(new[] { rp }));
      }

      return GH_ObjectResponse.Handled;
    }
  }
}
