using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace Telepathy
{
  internal static class TelepathyIcons
  {
    /// <summary>Library icon in the Grasshopper file browser (sender artwork).</summary>
    internal static Bitmap AssemblyIcon => Sender;

    internal static Bitmap Sender => _sender ??= LoadEmbedded("sender.png");
    internal static Bitmap Receiver => _receiver ??= LoadEmbedded("receiver.png");
    internal static Bitmap Explode => _explode ??= LoadEmbedded("explode.png");

    private static Bitmap? _sender;
    private static Bitmap? _receiver;
    private static Bitmap? _explode;

    private const int GrasshopperIconSize = 24;

    private static Bitmap LoadEmbedded(string fileName)
    {
      var asm = Assembly.GetExecutingAssembly();
      using Stream? stream = asm.GetManifestResourceStream($"Telepathy.Resources.{fileName}");
      if (stream is null)
        return new Bitmap(GrasshopperIconSize, GrasshopperIconSize);

      using var ms = new MemoryStream();
      stream.CopyTo(ms);
      ms.Position = 0;
      using var raw = new Bitmap(ms);

      if (raw.Width == GrasshopperIconSize && raw.Height == GrasshopperIconSize)
        return new Bitmap(raw);

      var scaled = new Bitmap(GrasshopperIconSize, GrasshopperIconSize);
      using (var g = Graphics.FromImage(scaled))
      {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(raw, 0, 0, GrasshopperIconSize, GrasshopperIconSize);
      }

      return scaled;
    }
  }
}
