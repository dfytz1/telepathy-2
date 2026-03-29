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
      "Remote sender/receiver parameters that auto-wire by matching keys. Compatible with definitions built using the original Telepathy plugin (same component identities).";

    public override Guid Id => new Guid("7A8B9C0D-1E2F-3A4B-5C6D-7E8F9A0B1C2D");

    public override string AuthorName => "GIA";

    public override string AuthorContact => "https://github.com/dfytz1/telepathy-2";

    public override string AssemblyVersion => GetType().Assembly.GetName().Version?.ToString() ?? "1.0";
  }
}
