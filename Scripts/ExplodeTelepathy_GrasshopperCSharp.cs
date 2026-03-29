// =============================================================================
// Single C# script component: replace Telepathy Remote Sender / Receiver params
// with standard Generic Data params (same wiring topology).
// =============================================================================
//
// Paste this entire file into the C# script editor (replace the default class).
// Grasshopper creates inputs/outputs from the RunScript signature:
//   ExecuteTelepathyReplacement (bool) → ReplacementStatus (text)
//
// Save a copy of your .gh before setting ExecuteTelepathyReplacement to true.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

public class Script_Instance : GH_ScriptInstance
{
  private static readonly Guid GuidRemoteSender   = new Guid("ADA99447-8A42-4C8E-BAA4-C8EF36A372B6");
  private static readonly Guid GuidRemoteReceiver = new Guid("08CDCD26-518A-4FE2-8313-A2DB5DCDF800");

  private void RunScript(bool ExecuteTelepathyReplacement, ref object ReplacementStatus)
  {
    ReplacementStatus = string.Empty;

    if (!ExecuteTelepathyReplacement)
    {
      ReplacementStatus = "Set ExecuteTelepathyReplacement to true to convert Telepathy params (save the file first).";
      return;
    }

    if (Iteration > 0)
      return;

    var doc = Grasshopper.Instances.ActiveCanvas?.Document;
    if (doc == null)
    {
      this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No active Grasshopper document.");
      ReplacementStatus = "No active Grasshopper document.";
      return;
    }

    ReplacementStatus = ExplodeTelepathyInDocument(doc);
  }

  private static string ExplodeTelepathyInDocument(GH_Document doc)
  {
    // Only top-level floating params — Telepathy senders/receivers are never nested inside components.
    var floating = doc.Objects.OfType<IGH_Param>().ToList();

    var senders   = floating.Where(p => p.ComponentGuid == GuidRemoteSender).ToList();
    var receivers = floating.Where(p => p.ComponentGuid == GuidRemoteReceiver).ToList();

    if (senders.Count == 0 && receivers.Count == 0)
      return "No Telepathy Remote Sender or Receiver parameters found in this document.";

    var senderSet = new HashSet<IGH_Param>(senders);
    var log = new StringBuilder();
    log.AppendLine($"Found {senders.Count} sender(s), {receivers.Count} receiver(s).");

    // Create one Generic Data replacement per receiver.
    var receiverReplacements = new Dictionary<IGH_Param, Param_GenericObject>();

    foreach (var receiver in receivers)
    {
      var replacement = new Param_GenericObject();
      CopyParamSurface(receiver, replacement);
      receiverReplacements[receiver] = replacement;
      doc.AddObject(replacement, false, doc.ObjectCount);
    }

    // Wire each replacement directly to the sender's upstream sources,
    // bypassing the Telepathy sender in the chain.
    foreach (var receiver in receivers)
    {
      var replacement = receiverReplacements[receiver];

      log.Append($"  Receiver '{receiver.NickName}': {receiver.Sources.Count} source(s)");

      foreach (var src in receiver.Sources.ToList())
      {
        if (senderSet.Contains(src))
        {
          log.Append($" [sender '{src.NickName}' has {src.Sources.Count} upstream(s)]");
          foreach (var upstreamSource in src.Sources.ToList())
          {
            if (!replacement.Sources.Contains(upstreamSource))
              replacement.AddSource(upstreamSource);
          }
        }
        else
        {
          // Not a Telepathy sender — wire directly.
          if (!replacement.Sources.Contains(src))
            replacement.AddSource(src);
        }
      }

      log.AppendLine($" → replacement now has {replacement.Sources.Count} source(s).");
    }

    // Rewire downstream: use Recipients on each receiver — these are exactly
    // the params that currently point to this receiver as their source.
    foreach (var receiver in receivers)
    {
      var replacement = receiverReplacements[receiver];

      foreach (var recipient in receiver.Recipients.ToList())
      {
        if (!recipient.Sources.Contains(receiver))
          continue;

        recipient.Sources.Remove(receiver);
        receiver.Recipients.Remove(recipient);

        if (!recipient.Sources.Contains(replacement))
          recipient.AddSource(replacement);
      }
    }

    // Clear default volatile data so the next solution collects from sources only.
    foreach (var replacement in receiverReplacements.Values)
    {
      replacement.ClearData();
      replacement.ExpireSolution(false);
    }

    // Remove all Telepathy params.
    foreach (var sender in senders)
      doc.RemoveObject(sender, true);
    foreach (var receiver in receivers)
      doc.RemoveObject(receiver, true);

    doc.ScheduleSolution(20);

    log.AppendLine($"Done. {receivers.Count} receiver(s) replaced, {senders.Count} sender(s) removed.");
    return log.ToString();
  }

  private static void CopyParamSurface(IGH_Param oldParam, Param_GenericObject newParam)
  {
    newParam.Name        = oldParam.Name;
    newParam.NickName    = oldParam.NickName;
    newParam.Description = oldParam.Description;
    if (oldParam is IGH_PreviewObject oldPv && newParam is IGH_PreviewObject newPv)
      newPv.Hidden = oldPv.Hidden;
    newParam.WireDisplay = oldParam.WireDisplay;

    newParam.CreateAttributes();
    if (oldParam.Attributes != null && newParam.Attributes != null)
    {
      newParam.Attributes.Pivot = oldParam.Attributes.Pivot;
      newParam.Attributes.ExpireLayout();
    }
  }
}
