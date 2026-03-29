using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Linq;

namespace Telepathy
{
  public class ExplodeTelepathyComponent : GH_Component
  {
    public ExplodeTelepathyComponent()
      : base(
          "Explode Telepathy",
          "ExplTP",
          "Removes all Remote Sender / Remote Receiver parameters and replaces them with direct wires. " +
          "Connect a True Only button to Execute — using a Toggle risks re-running on every solution. " +
          "Supports Ctrl+Z undo (one step per key).",
          "Params",
          "Telepathy")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddBooleanParameter(
        "ExecuteReplacement",
        "Execute",
        "Connect a True Only button (not a Toggle) and click it once to replace all Telepathy params " +
        "with direct wires. Supports Ctrl+Z undo (one step per key).",
        GH_ParamAccess.item,
        false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter(
        "ReplacementStatus",
        "Status",
        "Summary of what was replaced.",
        GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var execute = false;
      DA.GetData(0, ref execute);

      if (!execute)
      {
        DA.SetData(0, "Set Execute to True to convert all Telepathy params to direct wires.");
        return;
      }

      var doc = Instances.ActiveCanvas?.Document;
      if (doc == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No active Grasshopper document found.");
        DA.SetData(0, "No active Grasshopper document.");
        return;
      }

      // Snapshot all distinct keys before we start removing anything.
      var keys = doc.Objects
        .OfType<RemoteParam>()
        .Select(p => p.NickName)
        .Where(k => !string.IsNullOrEmpty(k))
        .Distinct()
        .ToList();

      if (keys.Count == 0)
      {
        DA.SetData(0, "No Telepathy params found in this document.");
        return;
      }

      // ExplodeKey handles correct rewiring + undo for each key (same as right-click Remove).
      foreach (var key in keys)
        TelepathyUtils.ExplodeKey(doc, key);

      DA.SetData(0, $"Exploded {keys.Count} key(s): {string.Join(", ", keys)}.");
    }

    public override GH_Exposure Exposure => GH_Exposure.septenary;

    public override Guid ComponentGuid => new Guid("3F8A1C2D-4B5E-4F6A-9C7D-8E0F1A2B3C4D");

    protected override Bitmap Icon => TelepathyIcons.Explode;
  }
}
