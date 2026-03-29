using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace Telepathy
{
  public class TelepathyInfo : GH_AssemblyInfo
  {
    public override string Name => "Telepathy 2";

    public override Bitmap Icon => TelepathyIcons.AssemblyIcon;

    public override string Description =>
      "Facilitates automatic wireless connection between special parameters (Telepathy for Grasshopper 1).";

    public override Guid Id => new Guid("7A8B9C0D-1E2F-3A4B-5C6D-7E8F9A0B1C2D");

    public override string AuthorName => "Andrew Heumann (ported for Rhino 8 / Mac)";

    public override string AuthorContact => "https://github.com/andrewheumann/telepathy";

    public override string AssemblyVersion => GetType().Assembly.GetName().Version?.ToString() ?? "1.0";
  }
}
