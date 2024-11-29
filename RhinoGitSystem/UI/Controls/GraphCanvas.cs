using System;
using System.Collections.Generic;
using Eto.Forms;
using Eto.Drawing;
using System.Linq;

namespace RhinoGitSystem.UI.Controls
{
    public class GraphCanvas : Drawable
    {
        private readonly List<CommitNode> nodes = new List<CommitNode>();
        private CommitNode selectedNode;
        private readonly Form parentForm;

        public event EventHandler<CommitNode> CommitSelected;

        public GraphCanvas(Form parent)
        {
            parentForm = parent;
            Size = new Size(1200, 800);

            MouseDown += GraphCanvas_MouseDown;
            MouseMove += GraphCanvas_MouseMove;
        }

        public void SetNodes(List<CommitNode> newNodes)
        {
            nodes.Clear();
            nodes.AddRange(newNodes);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Colors.White);

            // ブランチ間の接続線を描画
            foreach (var node in nodes)
            {
                var parentNode = nodes.FirstOrDefault(n => n.CommitId == node.ParentCommit);
                if (parentNode != null && parentNode.BranchName != node.BranchName)
                {
                    using (var pen = new Pen(node.Color, 2))
                    {
                        var midX = (parentNode.Position.X + node.Position.X) / 2;
                        var points = new[]
                        {
                            parentNode.Position,
                            new Point(midX, parentNode.Position.Y),
                            new Point(midX, node.Position.Y),
                            node.Position
                        };

                        for (int i = 0; i < points.Length - 1; i++)
                        {
                            g.DrawLine(pen, points[i], points[i + 1]);
                        }
                    }
                }
            }

            // ブランチ内の接続線を描画
            foreach (var branchGroup in nodes.GroupBy(n => n.BranchName))
            {
                var branchNodes = branchGroup.OrderBy(n => n.Timestamp).ToList();
                if (branchNodes.Count > 1)
                {
                    using (var pen = new Pen(branchNodes[0].Color, 3))
                    {
                        for (int i = 0; i < branchNodes.Count - 1; i++)
                        {
                            g.DrawLine(pen,
                                branchNodes[i].Position,
                                branchNodes[i + 1].Position);
                        }
                    }
                }
            }

            // マージ線を描画
            foreach (var node in nodes.Where(n => n.IsMergePoint && n.MergeSourceNode != null))
            {
                using (var pen = new Pen(node.Color, 2))
                {
                    var sourceNode = node.MergeSourceNode;
                    var midX = (sourceNode.Position.X + node.Position.X) / 2;
                    var points = new[]
                    {
                        sourceNode.Position,
                        new Point(midX, sourceNode.Position.Y),
                        new Point(midX, node.Position.Y),
                        node.Position
                    };

                    for (int i = 0; i < points.Length - 1; i++)
                    {
                        g.DrawLine(pen, points[i], points[i + 1]);
                    }
                }
            }

            // ノードを描画
            foreach (var node in nodes)
            {
                DrawNode(g, node, node == selectedNode);
            }

            // デバッグ情報を表示
            using (var brush = new SolidBrush(Colors.Black))
            using (var font = new Font(SystemFonts.Default().Family, 8))
            {
                var debugY = 10;
                foreach (var branchGroup in nodes.GroupBy(n => n.BranchName))
                {
                    g.DrawText(font, brush, new PointF(10, debugY),
                        $"Branch {branchGroup.Key}: {branchGroup.Count()} nodes");
                    debugY += 15;
                }
            }
        }

        private void DrawNode(Graphics g, CommitNode node, bool isSelected)
        {
            var nodeRect = new RectangleF(
                node.Position.X - 5,
                node.Position.Y - 5,
                10, 10);

            using (var brush = new SolidBrush(isSelected ? Colors.Blue : node.Color))
            using (var pen = new Pen(Colors.Black, 1))
            {
                g.FillEllipse(brush, nodeRect);
                g.DrawEllipse(pen, nodeRect);
            }

            string displayMessage = FormatCommitMessage(node.Message, node.CommitId);

            using (var brush = new SolidBrush(Colors.Black))
            using (var font = new Font(SystemFonts.Default().Family, 8))
            {
                var messageRect = new RectangleF(
                    node.Position.X - 100,
                    node.Position.Y - 25,
                    200,
                    20);

                g.DrawText(font, brush, messageRect, displayMessage);
            }
        }

        private string FormatCommitMessage(string message, string commitId)
        {
            if (message.StartsWith("Auto-save before switching"))
            {
                return $"Auto-save ({commitId.Substring(0, 7)})";
            }
            return $"{message} ({commitId.Substring(0, 7)})";
        }

        private void GraphCanvas_MouseDown(object sender, MouseEventArgs e)
        {
            var clickedNode = FindNodeAtPoint(e.Location);
            if (clickedNode != null)
            {
                selectedNode = clickedNode;
                CommitSelected?.Invoke(this, clickedNode);
                Invalidate();
            }
        }

        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var hoveredNode = FindNodeAtPoint(e.Location);
            Cursor = hoveredNode != null ? Cursors.Pointer : Cursors.Default;
        }

        private CommitNode FindNodeAtPoint(PointF point)
        {
            foreach (var node in nodes)
            {
                var nodeRect = new RectangleF(
                    node.Position.X - 5,
                    node.Position.Y - 5,
                    10, 10);

                if (nodeRect.Contains(point))
                {
                    return node;
                }
            }
            return null;
        }
    }
}
