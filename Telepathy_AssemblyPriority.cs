using Grasshopper;
using Grasshopper.Kernel;
using System;

namespace Telepathy
{
  public class Telepathy_AssemblyPriority : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      Grasshopper.Instances.ComponentServer.AddAlias("send", new Guid("ADA99447-8A42-4C8E-BAA4-C8EF36A372B6"));
      Grasshopper.Instances.ComponentServer.AddAlias("rec", new Guid("08CDCD26-518A-4FE2-8313-A2DB5DCDF800"));

      // Register Cmd+T / Ctrl+T shortcut once the canvas is created.
      Grasshopper.Instances.CanvasCreated += TelepathyShortcut.RegisterOn;

      return GH_LoadingInstruction.Proceed;
    }
  }
}
