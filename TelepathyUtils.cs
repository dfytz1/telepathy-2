using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Telepathy
{
  public static class TelepathyUtils
  {
    public static void ConnectMatchingParams(GH_Document doc, bool scheduleNew)
    {
      if (doc == null) return;
      ConnectMatchingParams(doc);
      if (scheduleNew)
        doc.ScheduleSolution(10);
    }

    /// <summary>
    /// Wires receivers to all senders whose nickname matches the receiver key (ordinal;
    /// <c>*</c> and <c>?</c> wildcards supported).
    /// </summary>
    public static void ConnectMatchingParams(GH_Document doc)
    {
      if (doc == null) return;

      var activeObjects = doc.ActiveObjects();
      var allReceivers  = activeObjects.OfType<Param_RemoteReceiver>().ToList();
      var allSenders    = activeObjects.OfType<Param_RemoteSender>().ToList();

      foreach (var receiver in allReceivers)
        ProcessReceiver(allSenders, receiver);
    }

    public static void ProcessReceiver(List<Param_RemoteSender> allSenders, Param_RemoteReceiver receiver)
    {
      var key = receiver.NickName;
      if (string.IsNullOrEmpty(key))
        return;

      var sourcesToRemove = new List<IGH_Param>();
      foreach (IGH_Param param in receiver.Sources)
      {
        if (!NickNamesMatch(param.NickName, key))
          sourcesToRemove.Add(param);
        else if (param is Param_RemoteSender snd && snd.GroupPalette != receiver.GroupPalette)
          sourcesToRemove.Add(param);
      }

      RemoveSources(receiver, sourcesToRemove);

      foreach (var sender in allSenders.Where(s =>
        NickNamesMatch(s.NickName, key) && s.GroupPalette == receiver.GroupPalette))
      {
        if (!receiver.Sources.Contains(sender))
          receiver.AddSource(sender);
      }
    }

    /// <summary>
    /// Matches a sender <paramref name="nick"/> against a receiver <paramref name="pattern"/>.
    ///
    /// Three modes, chosen by the pattern's prefix/suffix:
    ///   /regex/   — raw .NET regex (e.g. <c>/Top|Front/</c>, <c>/(?i)tube.*/</c>)
    ///   glob      — <c>*</c> = any chars, <c>?</c> = one char  (e.g. <c>Top *</c>)
    ///   literal   — exact ordinal match (fastest; used when no wildcards present)
    /// </summary>
    internal static bool NickNamesMatch(string? nick, string pattern)
    {
      if (nick == null) return false;

      // ── Regex mode: pattern wrapped in /…/ ──────────────────────────────────
      if (pattern.Length >= 2 && pattern[0] == '/' && pattern[pattern.Length - 1] == '/')
      {
        var rx = GetOrCompileRegex(pattern.Substring(1, pattern.Length - 2));
        return rx != null && rx.IsMatch(nick);
      }

      // ── Literal mode: no wildcards ───────────────────────────────────────────
      if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
        return string.Equals(nick, pattern, StringComparison.Ordinal);

      // ── Glob mode: * and ? wildcards — compile once per distinct pattern ─────
      var globRx = GetOrCompileRegex(GlobToRegex(pattern));
      return globRx != null && globRx.IsMatch(nick);
    }

    // Cache compiled Regex objects keyed by their source string.
    private static readonly Dictionary<string, Regex?> _regexCache = new Dictionary<string, Regex?>();

    private static Regex? GetOrCompileRegex(string source)
    {
      if (_regexCache.TryGetValue(source, out var cached))
        return cached;
      Regex? rx = null;
      try { rx = new Regex(source, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
      catch (ArgumentException) { /* malformed — store null so we don't retry */ }
      _regexCache[source] = rx;
      return rx;
    }

    private static string GlobToRegex(string pattern)
    {
      var sb = new StringBuilder("^");
      foreach (var c in pattern)
      {
        switch (c)
        {
          case '*':  sb.Append(".*");           break;
          case '?':  sb.Append('.');            break;
          case '.':
          case '+':
          case '$':
          case '^':
          case '[':
          case ']':
          case '(':
          case ')':
          case '{':
          case '}':
          case '|':
          case '\\': sb.Append('\\').Append(c); break;
          default:   sb.Append(c);              break;
        }
      }
      sb.Append('$');
      return sb.ToString();
    }

    public static void RemoveSources(IGH_Param target, List<IGH_Param> sources)
    {
      var removed = false;
      foreach (var source in sources)
      {
        if (source == null) continue;
        if (!target.Sources.Contains(source)) continue;
        target.Sources.Remove(source);
        source.Recipients.Remove(target);
        removed = true;
      }

      if (removed)
      {
        target.OnObjectChanged(GH_ObjectEventType.Sources);
        target.ExpireSolution(false);
      }
    }

    internal static string GetLastUsedKey(GH_Document doc) => GetAllKeys(doc).LastOrDefault() ?? string.Empty;

    public static List<string> GetAllKeys(GH_Document? doc)
    {
      if (doc == null) return new List<string>();
      return doc.ActiveObjects()
        .OfType<RemoteParam>()
        .Select(o => o.NickName)
        .Distinct()
        .ToList();
    }

    // ── Undo support ────────────────────────────────────────────────────────────

    /// <summary>Captures a parameter's Sources list so it can be restored on undo.</summary>
    private sealed class ParamWiringSnapshot
    {
      public readonly IGH_Param Param;
      private readonly List<IGH_Param> _originalSources;

      public ParamWiringSnapshot(IGH_Param param)
      {
        Param = param;
        _originalSources = param.Sources.ToList();
      }

      public void Restore()
      {
        // Disconnect everything currently wired in.
        foreach (var src in Param.Sources.ToList())
        {
          Param.Sources.Remove(src);
          src.Recipients.Remove(Param);
        }
        // Reconnect the original sources (AddSource updates both sides).
        foreach (var src in _originalSources)
          if (!Param.Sources.Contains(src))
            Param.AddSource(src);

        Param.OnObjectChanged(GH_ObjectEventType.Sources);
        Param.ExpireSolution(false);
      }
    }

    private sealed class ExplodeKeyUndoAction : IGH_UndoAction
    {
      private readonly string _key;
      private readonly List<(IGH_Param Param, System.Drawing.PointF Pivot)> _removed;
      private readonly List<ParamWiringSnapshot> _snapshots;

      public ExplodeKeyUndoAction(string key,
        List<(IGH_Param, System.Drawing.PointF)> removed,
        List<ParamWiringSnapshot> snapshots)
      {
        _key = key;
        _removed = removed;
        _snapshots = snapshots;
      }

      public string Description => $"Remove Telepathy '{_key}'";
      public bool ExpiresSolution => true;
      public bool ExpiresDisplay  => false;
      public GH_UndoState State   => (GH_UndoState)0;
      public void Flush() { }
      public bool Write(GH_IWriter writer) => true;
      public bool Read(GH_IReader reader)  => true;

      public void Undo(GH_Document doc)
      {
        // Re-add senders and receivers first (snapshots reference them).
        foreach (var (param, pivot) in _removed)
        {
          doc.AddObject(param, false);
          param.Attributes.Pivot = pivot;
          param.Attributes.ExpireLayout();
        }
        // Restore wiring of every affected downstream/upstream param.
        foreach (var snap in _snapshots)
          snap.Restore();

        doc.ScheduleSolution(50);
      }

      public void Redo(GH_Document doc) => ExplodeKey(doc, _key);
    }

    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rewires all consumers of receivers whose key matches <paramref name="key"/> directly
    /// to the upstream producers (bypassing the Telepathy channel), then removes those
    /// senders and receivers from the document. The operation is fully undoable.
    /// </summary>
    public static void ExplodeKey(GH_Document doc, string key)
    {
      if (doc == null) return;

      var allObjects = doc.Objects.OfType<IGH_Param>().ToList();
      var senders   = allObjects.OfType<Param_RemoteSender>()
                                .Where(s => s.NickName == key)
                                .Cast<IGH_Param>().ToList();
      var receivers = allObjects.OfType<Param_RemoteReceiver>()
                                .Where(r => r.NickName == key)
                                .Cast<IGH_Param>().ToList();

      if (senders.Count == 0 && receivers.Count == 0) return;

      var senderSet = new HashSet<IGH_Param>(senders);

      // ── Snapshot state BEFORE any changes ────────────────────────────────────
      var seen      = new HashSet<Guid>();
      var snapshots = new List<ParamWiringSnapshot>();

      void TrySnapshot(IGH_Param p)
      {
        if (p == null || !seen.Add(p.InstanceGuid)) return;
        snapshots.Add(new ParamWiringSnapshot(p));
      }

      // Snapshot senders and receivers themselves — doc.RemoveObject clears their
      // Sources and upstreams' Recipients, so we need to restore them on undo.
      foreach (var sender in senders)   TrySnapshot(sender);
      foreach (var receiver in receivers) TrySnapshot(receiver);

      // Snapshot every downstream consumer of each receiver.
      foreach (var receiver in receivers)
        foreach (var recipient in receiver.Recipients)
          TrySnapshot(recipient);

      // Snapshot every non-receiver recipient of each sender.
      foreach (var sender in senders)
        foreach (var r in sender.Recipients.Where(r => !receivers.Contains(r)))
          TrySnapshot(r);

      // Collect removed objects with their current pivot for undo re-placement.
      var removed = new List<(IGH_Param, System.Drawing.PointF)>();
      foreach (var s in senders)
        removed.Add((s, s.Attributes?.Pivot ?? System.Drawing.PointF.Empty));
      foreach (var r in receivers)
        removed.Add((r, r.Attributes?.Pivot ?? System.Drawing.PointF.Empty));

      // Register undo record BEFORE modifying anything.
      var undoRecord = new GH_UndoRecord($"Remove Telepathy '{key}'");
      undoRecord.AddAction(new ExplodeKeyUndoAction(key, removed, snapshots));
      doc.UndoUtil.RecordEvent(undoRecord);

      // ── Now apply the changes ─────────────────────────────────────────────────
      foreach (var receiver in receivers)
      {
        var upstreams = new List<IGH_Param>();
        foreach (var src in receiver.Sources.ToList())
        {
          if (senderSet.Contains(src))
          {
            foreach (var senderSrc in src.Sources.ToList())
              if (!upstreams.Contains(senderSrc))
                upstreams.Add(senderSrc);
          }
          else
          {
            if (!upstreams.Contains(src))
              upstreams.Add(src);
          }
        }

        foreach (var recipient in receiver.Recipients.ToList())
        {
          recipient.Sources.Remove(receiver);
          receiver.Recipients.Remove(recipient);
          foreach (var upstream in upstreams)
            if (!recipient.Sources.Contains(upstream))
              recipient.AddSource(upstream);
        }
      }

      foreach (var sender in senders)
      {
        var senderUpstreams = sender.Sources.ToList();
        foreach (var recipient in sender.Recipients
                   .Where(r => !receivers.Contains(r)).ToList())
        {
          recipient.Sources.Remove(sender);
          sender.Recipients.Remove(recipient);
          foreach (var upstream in senderUpstreams)
            if (!recipient.Sources.Contains(upstream))
              recipient.AddSource(upstream);
        }
      }

      // Pass false — undo is handled by our action above, not GH's auto-tracking.
      foreach (var s in senders)   doc.RemoveObject(s, false);
      foreach (var r in receivers) doc.RemoveObject(r, false);

      doc.ScheduleSolution(20);
    }

    internal static void FindReplace(string find, string replace, bool forceExact,
      bool includeSenders = true, bool includeReceivers = true)
    {
      var doc = Grasshopper.Instances.ActiveCanvas?.Document;
      if (doc == null) return;

      var targets = doc.ActiveObjects().OfType<RemoteParam>()
        .Where(p =>
        {
          if (!includeSenders   && p is Param_RemoteSender)   return false;
          if (!includeReceivers && p is Param_RemoteReceiver) return false;
          return forceExact ? p.NickName == find : p.NickName.Contains(find);
        })
        .ToList();

      if (targets.Count == 0) return;

      var before = RemoteParam.CaptureState(targets);

      foreach (var param in targets)
      {
        param.NickName = param.NickName.Replace(find, replace);
        param.Attributes.ExpireLayout();
      }

      RemoteParam.RecordUndo(doc, "Find/Replace Telepathy Keys",
        before, RemoteParam.CaptureState(targets));
    }
  }
}
