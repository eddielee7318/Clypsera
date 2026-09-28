using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal sealed class DocumentPickerForm : Form
    {
        private readonly TreeView tree;
        public string SelectedDocumentId { get; private set; }

        public DocumentPickerForm(List<LibraryNode> nodes, string currentDocumentId)
        {
            Text = "移动到文件夹或文档";
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 430;
            Height = 500;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(340, 380);

            tree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, FullRowSelect = true };
            TreeNode unfiled = new TreeNode("未归档") { Tag = "__unfiled__" };
            tree.Nodes.Add(unfiled);
            AddChildren(tree.Nodes, null, nodes);

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 54 };
            Button cancel = new Button { Text = "取消", Width = 82, Height = 32, Left = 238, Top = 10, Anchor = AnchorStyles.Top | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            Button ok = new Button { Text = "移动", Width = 82, Height = 32, Left = 330, Top = 10, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
            Controls.Add(tree); Controls.Add(bottom);
            tree.BringToFront();
            tree.ExpandAll();
            SelectCurrent(tree.Nodes, currentDocumentId);
            ok.Click += delegate { Confirm(); };
            tree.NodeMouseDoubleClick += delegate { Confirm(); };
            CancelButton = cancel;
            SettingsData appearance = AppData.LoadSettings();
            UiStyle.Prepare(this, appearance.AppTheme);
            Localizer.Apply(this);
        }

        private void AddChildren(TreeNodeCollection target, string parentId, List<LibraryNode> all)
        {
            foreach (LibraryNode node in all.Where(x => x.ParentId == parentId).OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name))
            {
                TreeNode treeNode = new TreeNode((node.IsFolder ? "📁 " : node.IsBook ? "📚 " : "📄 ") + node.Name) { Tag = node };
                target.Add(treeNode);
                if (node.IsFolder) AddChildren(treeNode.Nodes, node.Id, all);
            }
        }

        private void SelectCurrent(TreeNodeCollection nodes, string id)
        {
            foreach (TreeNode node in nodes)
            {
                LibraryNode value = node.Tag as LibraryNode;
                if ((value != null && value.Id == id) || (value == null && string.IsNullOrEmpty(id))) { tree.SelectedNode = node; return; }
                SelectCurrent(node.Nodes, id);
            }
        }

        private void Confirm()
        {
            if (tree.SelectedNode == null) return;
            if (Convert.ToString(tree.SelectedNode.Tag) == "__unfiled__") SelectedDocumentId = null;
            else
            {
                LibraryNode node = tree.SelectedNode.Tag as LibraryNode;
                if (node == null)
                {
                    MessageBox.Show("请选择一个文件夹或文档。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SelectedDocumentId = node.Id;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
