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
using RhinoGitSystem.Models;

namespace RhinoGitSystem.UI.Forms
{
    public class UnifiedCommitViewer : Form
    {
        private readonly RhinoDoc doc;
        private readonly GraphCanvas graphCanvas;
        private GridView branchGrid;
        private GridView commitGrid;
        private readonly List<CommitNode> nodes = new List<CommitNode>();
        private List<object> commitDataStore;
        private readonly Dictionary<string, Color> branchColors = new Dictionary<string, Color>();

        public UnifiedCommitViewer(RhinoDoc doc)
        {
            this.doc = doc;
            this.commitDataStore = new List<object>();
            Title = "Unified Commit Viewer";
            ClientSize = new Size(1400, 900);

            var mainLayout = new DynamicLayout();

            // メイン分割
            var mainSplitter = new Splitter
            {
                Orientation = Orientation.Vertical,
                Position = 450 // 全体の高さの半分
            };

            // 上部のグラフ表示部分
            var scrollable = new Scrollable
            {
                ExpandContentWidth = true,
                ExpandContentHeight = true,
                Border = BorderType.None
            };

            graphCanvas = new GraphCanvas(this);
            graphCanvas.CommitSelected += Canvas_CommitSelected;
            graphCanvas.Size = new Size(3000, 400);
            scrollable.Content = graphCanvas;

            mainSplitter.Panel1 = scrollable;

            // 下部のグリッド表示部分
            var lowerSection = new Splitter();
            lowerSection.Panel1 = CreateBranchSection();
            lowerSection.Panel2 = CreateCommitSection();
            lowerSection.Position = 300;

            mainSplitter.Panel2 = lowerSection;
            mainLayout.Add(mainSplitter, yscale: true);

            Content = mainLayout;
            LoadData();
        }

        private Control CreateBranchSection()
        {
            var branchSection = new DynamicLayout();
            branchSection.DefaultPadding = new Padding(5);
            branchSection.Add(new Label { Text = "Branches" });

            branchGrid = new GridView
            {
                ShowHeader = true,
                AllowMultipleSelection = false
            };

            branchGrid.SelectionChanged += BranchGrid_SelectionChanged;
            branchGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Branch Name",
                DataCell = new TextBoxCell { Binding = Binding.Property<BranchData, string>(r => r.Name) }
            });

            branchSection.Add(branchGrid, yscale: true);
            return branchSection;
        }

        private Control CreateCommitSection()
        {
            var commitSection = new DynamicLayout();
            commitSection.DefaultPadding = new Padding(5);
            commitSection.Add(new Label { Text = "Commits" });

            commitGrid = new GridView
            {
                ShowHeader = true,
                AllowMultipleSelection = true
            };

            commitGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Commit ID",
                DataCell = new TextBoxCell { Binding = Binding.Property<CommitData, string>(r => r.CommitId) },
                Width = 200
            });

            commitGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Message",
                DataCell = new TextBoxCell { Binding = Binding.Property<CommitData, string>(r => r.Message) },
                Width = 300
            });

            commitGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Author",
                DataCell = new TextBoxCell { Binding = Binding.Property<CommitData, string>(r => r.Author) },
                Width = 150
            });

            commitGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Date",
                DataCell = new TextBoxCell { Binding = Binding.Property<CommitData, string>(r => r.Timestamp) },
                Width = 150
            });

            var buttonLayout = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5 };
            var showCommitButton = new Button { Text = "Show Selected Commits" };
            showCommitButton.Click += ShowSelectedCommits_Click;
            var refreshButton = new Button { Text = "Refresh" };
            refreshButton.Click += (s, e) => LoadData();

            buttonLayout.Items.Add(showCommitButton);
            buttonLayout.Items.Add(refreshButton);

            commitSection.Add(commitGrid, yscale: true);
            commitSection.Add(buttonLayout);

            return commitSection;
        }

        private void LoadData()
        {
            RhinoApp.WriteLine("Before clearing branchColors - Count: " + branchColors.Count);
            branchColors.Clear();
            RhinoApp.WriteLine("After clearing branchColors - Count: " + branchColors.Count);

            var branchData = GetBranchData();
            branchGrid.DataStore = branchData;
            LoadCommits();

            // 既存の可視化をクリア
            var visualizationObjects = doc.Objects
                .Where(obj => obj.Attributes.GetUserString("CommitVisualization") == "true")
                .Select(obj => obj.Id)
                .ToList();

            foreach (var id in visualizationObjects)
            {
                doc.Objects.Delete(id, true);
            }

            doc.Views.Redraw();
        }

        private void LoadCommits()
        {
            var history = ModelDiffCommand.Instance.GetModelHistory();
            var branchLayers = new Dictionary<string, int>();
            var usedLayers = new HashSet<int>();
            var parentBranches = new Dictionary<string, string>();
            var xOffset = 50;
            nodes.Clear();

            RhinoApp.WriteLine("Starting LoadCommits - branchColors count: " + branchColors.Count);

            // マージポイントの追跡
            var mergePoints = new Dictionary<string, (string TargetBranch, string MergeCommitId)>();

            foreach (var commit in history.OrderBy(h => h.Timestamp))
            {
                if (!branchColors.ContainsKey(commit.BranchName))
                {
                    var newColor = GetNextBranchColor(branchColors.Count);
                    branchColors[commit.BranchName] = newColor;
                    RhinoApp.WriteLine($"Assigning new color to {commit.BranchName}: {newColor}");

                    if (commit.BranchName == "main")
                    {
                        branchLayers[commit.BranchName] = 0;
                        usedLayers.Add(0);
                    }
                    else
                    {
                        var parentCommit = history.FirstOrDefault(h => h.CommitId == commit.ParentCommit);
                        if (parentCommit != null)
                        {
                            parentBranches[commit.BranchName] = parentCommit.BranchName;

                            int parentLayer = branchLayers.ContainsKey(parentCommit.BranchName) ? branchLayers[parentCommit.BranchName] : 0;
                            int layer = 1;
                            bool layerAssigned = false;

                            while (!layerAssigned)
                            {
                                if (!usedLayers.Contains(parentLayer + layer))
                                {
                                    branchLayers[commit.BranchName] = parentLayer + layer;
                                    usedLayers.Add(parentLayer + layer);
                                    layerAssigned = true;
                                }
                                else if (!usedLayers.Contains(parentLayer - layer))
                                {
                                    branchLayers[commit.BranchName] = parentLayer - layer;
                                    usedLayers.Add(parentLayer - layer);
                                    layerAssigned = true;
                                }
                                layer++;
                            }
                        }
                    }
                }

                // マージコミットの処理
                if (commit.Message.StartsWith("Merge branch"))
                {
                    try
                    {
                        var sourceBranch = commit.Message.Split('\'')[1];
                        mergePoints[sourceBranch] = (commit.BranchName, commit.CommitId);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                }
            }

            // コミットノードの作成
            foreach (var commit in history.OrderBy(h => h.Timestamp))
            {
                int yPos = 300 + (branchLayers[commit.BranchName] * 100);
                bool isMergeCommit = commit.Message.StartsWith("Merge branch");

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
                    IsMergePoint = isMergeCommit,
                    ParentBranch = parentBranches.ContainsKey(commit.BranchName) ? parentBranches[commit.BranchName] : null
                };

                RhinoApp.WriteLine($"Node position for {node.BranchName}: ({xOffset}, {yPos})");

                nodes.Add(node);
                xOffset += 150;
            }

            // ブランチの接続とマージ関係の設定
            foreach (var node in nodes)
            {
                var parentNode = nodes.FirstOrDefault(n => n.CommitId == node.ParentCommit);
                if (parentNode != null)
                {
                    var isFirstInBranch = !nodes
                        .Where(n => n.BranchName == node.BranchName)
                        .Any(n => n.Timestamp < node.Timestamp);

                    if (isFirstInBranch)
                    {
                        node.ParentBranch = parentNode.BranchName;
                        RhinoApp.WriteLine($"Branch connection: {node.BranchName} -> {parentNode.BranchName}");
                    }
                }

                if (node.IsMergePoint)
                {
                    try
                    {
                        var sourceBranchName = node.Message.Split('\'')[1];
                        var sourceNode = nodes
                            .Where(n => n.BranchName == sourceBranchName)
                            .OrderByDescending(n => n.Timestamp)
                            .FirstOrDefault();

                        if (sourceNode != null)
                        {
                            node.MergeSourceNode = sourceNode;
                        }
                    }
                    catch (IndexOutOfRangeException)
                    {
                        continue;
                    }
                }
            }

            // キャンバスサイズの更新とノードの設定
            int maxX = nodes.Max(n => n.Position.X) + 200;
            int minY = nodes.Min(n => n.Position.Y) - 100;
            int maxY = nodes.Max(n => n.Position.Y) + 100;
            int totalHeight = maxY - minY;

            graphCanvas.Size = new Size(
                Math.Max(maxX, 3000),
                Math.Max(totalHeight + 400, 2000)
            );

            graphCanvas.SetNodes(nodes);
            graphCanvas.Invalidate();

            foreach (var node in nodes)
            {
                RhinoApp.WriteLine($"Node: {node.CommitId}, Branch: {node.BranchName}, Parent: {node.ParentCommit}");
            }
        }

        private void BranchGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (branchGrid.SelectedRow >= 0)
            {
                var selectedBranch = (BranchData)branchGrid.SelectedItem;
                LoadCommitsForBranch(selectedBranch.Name);
            }
        }

        private void LoadCommitsForBranch(string branchName)
        {
            var history = ModelDiffCommand.Instance.GetModelHistory();
            var branchCommits = history
                .Where(h => h.BranchName == branchName)
                .OrderByDescending(h => h.Timestamp)
                .ToList();

            var commitDataList = branchCommits.Select(h => new CommitData
            {
                CommitId = h.CommitId,
                Message = h.Message,
                Author = h.Author,
                Timestamp = h.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                Changes = h.Changes
            }).ToList();

            commitDataStore = new List<object>(commitDataList);
            commitGrid.DataStore = commitDataList;
        }

        private void ShowSelectedCommits_Click(object sender, EventArgs e)
        {
            if (commitGrid.SelectedRows != null && commitGrid.SelectedRows.Any())
            {
                var dataStore = commitGrid.DataStore as IList<CommitData>;
                var commits = commitGrid.SelectedRows
                    .Select(index => dataStore[(int)index])
                    .Where(commit => commit != null)
                    .ToList();

                if (commits.Any())
                {
                    ShowCommitsDiff(commits);
                }
            }
        }

        private void ShowCommitsDiff(List<CommitData> commits)
        {
            var existingVisualizations = doc.Objects
                .Where(obj => obj.Attributes.GetUserString("CommitVisualization") == "true")
                .Select(obj => obj.Id)
                .ToList();

            foreach (var id in existingVisualizations)
            {
                doc.Objects.Delete(id, true);
            }

            foreach (var commit in commits)
            {
                foreach (var change in commit.Changes)
                {
                    var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                    if (obj != null)
                    {
                        var attributes = new ObjectAttributes();
                        attributes.ColorSource = ObjectColorSource.ColorFromObject;
                        attributes.SetUserString("CommitVisualization", "true");

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
            }

            doc.Views.Redraw();
        }

        private void Canvas_CommitSelected(object sender, CommitNode commit)
        {
            ShowCommitDiff(commit);
        }

        private void ShowCommitDiff(CommitNode commit)
        {
            var visualizationObjects = doc.Objects
                .Where(obj => obj.Attributes.GetUserString("CommitVisualization") == "true")
                .Select(obj => obj.Id)
                .ToList();

            foreach (var id in visualizationObjects)
            {
                doc.Objects.Delete(id, true);
            }

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

        private List<BranchData> GetBranchData()
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            var history = ModelDiffCommand.Instance.GetModelHistory();

            return branches.Select(b =>
            {
                var latestCommit = history
                    .Where(h => h.BranchName == b.Name)
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefault();

                return new BranchData
                {
                    Name = b.Name,
                    LatestCommit = latestCommit?.CommitId ?? "N/A",
                    LastModified = latestCommit?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"
                };
            }).ToList();
        }

        private Color GetNextBranchColor(int index)
        {
            var colors = new[]
            {
                new Color(0.255f, 0.412f, 0.882f, 1.0f),   // Royal Blue
                new Color(0.196f, 0.804f, 0.196f, 1.0f),   // Lime Green
                new Color(1.0f, 0.271f, 0.0f, 1.0f),       // Orange Red
                new Color(0.502f, 0.0f, 0.502f, 1.0f),     // Purple
                new Color(0.118f, 0.565f, 1.0f, 1.0f),     // Dodger Blue
                new Color(1.0f, 0.549f, 0.0f, 1.0f),       // Dark Orange
                new Color(0.0f, 0.502f, 0.502f, 1.0f),     // Teal
                new Color(0.863f, 0.078f, 0.235f, 1.0f)    // Crimson
            };

            return colors[index % colors.Length];
        }

        private class BranchData
        {
            public string Name { get; set; }
            public string LatestCommit { get; set; }
            public string LastModified { get; set; }
        }

        private class CommitData
        {
            public string CommitId { get; set; }
            public string Message { get; set; }
            public string Author { get; set; }
            public string Timestamp { get; set; }
            public List<ObjectChange> Changes { get; set; }

            public DateTime GetDateTime()
            {
                return DateTime.ParseExact(Timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }
    }
}
