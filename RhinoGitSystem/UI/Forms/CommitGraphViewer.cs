using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Eto.Drawing;
using Rhino;
using Rhino.DocObjects;
using System.Globalization;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.UI.Controls;

namespace RhinoGitSystem.UI.Forms
{
    public class CommitGraphViewer : Form
    {
        private readonly RhinoDoc doc;
        private readonly GraphCanvas canvas;
        private readonly List<CommitNode> nodes = new List<CommitNode>();

        public CommitGraphViewer(RhinoDoc doc)
        {
            this.doc = doc;
            Title = "Commit Graph Viewer";
            ClientSize = new Size(1200, 800);

            // スクロール可能なコンテナを作成
            var scrollable = new Scrollable
            {
                ExpandContentWidth = true,
                ExpandContentHeight = true,
                Border = BorderType.None
            };

            // キャンバスを作成
            canvas = new GraphCanvas(this);
            canvas.CommitSelected += Canvas_CommitSelected;
            canvas.Size = new Size(3000, 2000); // より大きいサイズを設定

            scrollable.Content = canvas;

            // レイアウトの作成
            var layout = new DynamicLayout();
            layout.Add(scrollable, yscale: true);

            Content = layout;

            LoadCommits();
        }

        private void LoadCommits()
        {
            var history = ModelDiffCommand.Instance.GetModelHistory();
            var branchColors = new Dictionary<string, Color>();
            var branchLayers = new Dictionary<string, int>();
            var mergePoints = new Dictionary<string, string>();
            var xOffset = 50;
            nodes.Clear();

            // ブランチレイヤーの割り当てを設定
            foreach (var commit in history.OrderBy(h => h.Timestamp))
            {
                if (!branchColors.ContainsKey(commit.BranchName))
                {
                    branchColors[commit.BranchName] = GetNextBranchColor(branchColors.Count);

                    if (commit.BranchName == "main")
                    {
                        branchLayers[commit.BranchName] = 0;
                    }
                    else
                    {
                        if (!branchLayers.ContainsKey(commit.BranchName))
                        {
                            var availableLayers = Enumerable.Range(-5, 11)
                                .Where(l => l != 0)
                                .Except(branchLayers.Values)
                                .OrderBy(Math.Abs)
                                .ToList();

                            branchLayers[commit.BranchName] = availableLayers.FirstOrDefault() * 150;
                        }
                    }
                }

                if (commit.Message.StartsWith("Merge branch"))
                {
                    try
                    {
                        var sourceBranch = commit.Message.Split('\'')[1];
                        mergePoints[sourceBranch] = commit.BranchName;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                }
            }

            // コミットノードの配置
            foreach (var commit in history.OrderBy(h => h.Timestamp))
            {
                int yPos = 300;
                bool isMergeCommit = commit.Message.StartsWith("Merge branch");

                yPos += (branchLayers[commit.BranchName] * 100);

                var node = new CommitNode
                {
                    CommitId = commit.CommitId,
                    Message = commit.Message,
                    Author = commit.Author,
                    Timestamp = commit.Timestamp,
                    BranchName = commit.BranchName,
                    ParentCommit = commit.ParentCommit,
                    Position = new Point(xOffset, yPos),
                    Color = branchColors[commit.BranchName],
                    Changes = commit.Changes,
                    IsMergePoint = isMergeCommit
                };

                nodes.Add(node);
                xOffset += 100;
            }

            // マージコミットの検出と接続
            foreach (var commit in history.OrderBy(h => h.Timestamp))
            {
                if (commit.Message.StartsWith("Merge branch"))
                {
                    try
                    {
                        var sourceBranch = commit.Message.Split('\'')[1];
                        var mergeNode = nodes.FirstOrDefault(n => n.CommitId == commit.CommitId);
                        if (mergeNode != null)
                        {
                            mergeNode.IsMergePoint = true;
                            var sourceNode = nodes
                                .Where(n => n.BranchName == sourceBranch)
                                .OrderByDescending(n => n.Timestamp)
                                .FirstOrDefault();

                            if (sourceNode != null)
                            {
                                mergeNode.MergeSourceNode = sourceNode;
                            }
                        }
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                }
            }

            // ブランチ接続の調整
            foreach (var node in nodes)
            {
                var parentNode = nodes.FirstOrDefault(n => n.CommitId == node.ParentCommit);
                if (parentNode != null && parentNode.BranchName != node.BranchName)
                {
                    // 新しいブランチの開始点を検出
                    var isFirstInBranch = !nodes
                        .Where(n => n.BranchName == node.BranchName && n.Timestamp < node.Timestamp)
                        .Any();

                    if (isFirstInBranch)
                    {
                        float midX = (parentNode.Position.X + node.Position.X) / 2;
                        node.BranchPoint = new PointF(midX, parentNode.Position.Y);
                        node.ParentBranch = parentNode.BranchName;
                    }
                }
            }

            // キャンバスサイズの更新
            int maxX = nodes.Max(n => n.Position.X) + 200;
            int minY = nodes.Min(n => n.Position.Y) - 100;
            int maxY = nodes.Max(n => n.Position.Y) + 100;
            int totalHeight = maxY - minY;

            canvas.Size = new Size(
                Math.Max(maxX, 3000),
                Math.Max(totalHeight + 400, 2000)
            );

            canvas.SetNodes(nodes);
            canvas.Invalidate();
        }

        private Color GetNextBranchColor(int index)
        {
            var colors = new[] {
                new Color(0.255f, 0.412f, 0.882f, 1.0f),   // Royal Blue
                new Color(0.196f, 0.804f, 0.196f, 1.0f),   // Lime Green
                new Color(1.0f, 0.271f, 0.0f, 1.0f),       // Orange Red
                new Color(0.502f, 0.0f, 0.502f, 1.0f),     // Purple
                new Color(0.118f, 0.565f, 1.0f, 1.0f),     // Dodger Blue
                new Color(1.0f, 0.549f, 0.0f, 1.0f),       // Dark Orange
                new Color(0.0f, 0.502f, 0.502f, 1.0f),     // Teal
                new Color(0.863f, 0.078f, 0.235f, 1.0f)    // Crimson
            };

            var color = colors[index % colors.Length];
            RhinoApp.WriteLine($"GetNextBranchColor({index}) returning: {color}");
            return color;
        }

        private void Canvas_CommitSelected(object sender, CommitNode commit)
        {
            // コミットの変更を表示
            ShowCommitDiff(commit);
        }

        private void ShowCommitDiff(CommitNode commit)
        {
            // 既存の表示をクリア
            var visualizationObjects = doc.Objects
                .Where(obj => obj.Attributes.GetUserString("CommitVisualization") == "true")
                .Select(obj => obj.Id)
                .ToList();

            foreach (var id in visualizationObjects)
            {
                doc.Objects.Delete(id, true);
            }

            // 新しい変更を表示
            foreach (var change in commit.Changes)
            {
                var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                if (obj != null)
                {
                    var attributes = new ObjectAttributes();
                    attributes.SetUserString("CommitVisualization", "true");
                    attributes.ColorSource = ObjectColorSource.ColorFromObject;

                    switch (change.ChangeType)
                    {
                        case "Added":
                            attributes.ObjectColor = System.Drawing.Color.Green;
                            break;
                        case "Modified":
                            attributes.ObjectColor = System.Drawing.Color.Yellow;
                            break;
                        case "Deleted":
                            attributes.ObjectColor = System.Drawing.Color.FromArgb(128, System.Drawing.Color.Red);
                            break;
                    }

                    doc.Objects.Add(obj, attributes);
                }
            }

            doc.Views.Redraw();
        }
    }
}
