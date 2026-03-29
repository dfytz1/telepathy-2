using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Telepathy
{
  internal static class KeyPickerUi
  {
    /// <summary>
    /// Shows a searchable key picker with built-in find/replace.
    /// Single-click a key → fills the Find bar.
    /// Double-click or Apply → sets the key (returns it) and closes.
    /// Replace button → runs find/replace without closing.
    /// Returns the chosen/typed key, or null if cancelled.
    /// </summary>
    internal static string? ShowPicker(IEnumerable<string> allKeys, string currentKey)
    {
      string? result = null;

      List<string> keys = allKeys
        .Where(k => !string.IsNullOrEmpty(k))
        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
        .ToList();

      // ── Controls ─────────────────────────────────────────────────────────────
      var findBox = new TextBox
      {
        Text            = currentKey,
        PlaceholderText = "Search / type a new key…"
      };
      var replaceBox = new TextBox
      {
        PlaceholderText = "Replace with…"
      };
      var listBox       = new ListBox();
      var inclSenders   = new CheckBox { Text = "Senders",     Checked = true  };
      var inclReceivers = new CheckBox { Text = "Receivers",   Checked = true  };
      var exactMatch    = new CheckBox { Text = "Exact match", Checked = true  };

      var replaceBtn = new Button { Text = "Replace" };
      var applyBtn   = new Button { Text = "Apply"   };
      var cancelBtn  = new Button { Text = "Close"   };

      // ── List helpers ──────────────────────────────────────────────────────────
      bool syncing = false;

      void RefreshList(string filter)
      {
        syncing = true;
        listBox.Items.Clear();
        if (string.IsNullOrEmpty(filter))
        {
          foreach (var k in keys) listBox.Items.Add(k);
        }
        else
        {
          // Matches float to the top; non-matches are still shown below.
          var matches = keys.Where(k => k.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
          var rest    = keys.Where(k => k.IndexOf(filter, StringComparison.OrdinalIgnoreCase) <  0).ToList();
          foreach (var k in matches) listBox.Items.Add(k);
          foreach (var k in rest)    listBox.Items.Add(k);
        }
        syncing = false;
      }

      RefreshList(currentKey);

      // Highlight current key — search in the displayed (possibly reordered) list.
      for (int i = 0; i < listBox.Items.Count; i++)
      {
        if (listBox.Items[i].Text == currentKey)
        {
          listBox.SelectedIndex = i;
          break;
        }
      }

      // ── Layout ────────────────────────────────────────────────────────────────
      var layout = new DynamicLayout
      {
        Padding        = new Padding(10),
        DefaultSpacing = new Size(5, 6)
      };
      layout.AddRow(findBox);
      layout.AddRow(replaceBox);
      layout.BeginVertical(yscale: true);
      layout.AddRow(listBox);
      layout.EndVertical();
      layout.AddRow(new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing     = 12,
        Items       = { inclSenders, inclReceivers, exactMatch }
      });
      layout.AddSeparateRow(replaceBtn, null, cancelBtn, applyBtn);

      var dialog = new Dialog
      {
        Title      = "Set Key",
        ClientSize = new Size(310, 420),
        Content    = layout,
        Resizable  = true
      };

      // ── Event handlers ────────────────────────────────────────────────────────

      // Typing in the find box filters the list.
      findBox.TextChanged += (_, _) =>
      {
        if (!syncing) RefreshList(findBox.Text);
      };

      // Single-click: populate find box (does NOT close).
      listBox.SelectedIndexChanged += (_, _) =>
      {
        if (syncing || listBox.SelectedIndex < 0) return;
        syncing = true;
        findBox.Text = listBox.Items[listBox.SelectedIndex].Text;
        syncing = false;
      };

      void Apply()
      {
        var text = findBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
          result = text;
        dialog.Close();
      }

      void DoReplace()
      {
        var findText    = findBox.Text    ?? "";
        var replaceText = replaceBox.Text ?? "";
        if (string.IsNullOrEmpty(findText)) return;

        TelepathyUtils.FindReplace(
          findText, replaceText,
          exactMatch.Checked    == true,
          inclSenders.Checked   != false,
          inclReceivers.Checked != false);

        // Refresh the key list to reflect renamed keys.
        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc != null)
        {
          keys = TelepathyUtils.GetAllKeys(doc)
            .Where(k => !string.IsNullOrEmpty(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
          RefreshList(findBox.Text ?? "");
        }
      }

      replaceBtn.Click += (_, _) => DoReplace();
      applyBtn.Click   += (_, _) => Apply();
      cancelBtn.Click  += (_, _) => dialog.Close();

      // Double-click list item → apply and close.
      listBox.MouseDoubleClick += (_, _) => Apply();

      // Keyboard shortcuts in the find box.
      findBox.KeyDown += (_, e) =>
      {
        switch (e.Key)
        {
          case Keys.Enter:
            Apply();
            break;
          case Keys.Escape:
            dialog.Close();
            break;
          case Keys.Down:
            if (listBox.Items.Count > 0)
            {
              listBox.Focus();
              if (listBox.SelectedIndex < 0)
                listBox.SelectedIndex = 0;
            }
            break;
        }
      };

      // Enter/Escape in the list.
      listBox.KeyDown += (_, e) =>
      {
        if (e.Key == Keys.Enter)  Apply();
        if (e.Key == Keys.Escape) dialog.Close();
      };

      try   { dialog.ShowModal(RhinoEtoApp.MainWindow); }
      catch (InvalidOperationException) { dialog.ShowModal(); }

      return result;
    }
  }
}
