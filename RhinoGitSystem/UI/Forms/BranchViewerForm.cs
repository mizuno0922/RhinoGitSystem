using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Eto.Drawing;
using Rhino;
using Rhino.DocObjects;
using System.Globalization;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.UI.Forms
{
    public class BranchViewerForm : Form
    {
        private GridView branchGrid;
        private GridView commitGrid;
        private TextBox searchBox;
        private RhinoDoc doc;
        private List<object> commitDataStore;

        public BranchViewerForm(RhinoDoc doc)
        {
            this.doc = doc;
            this.commitDataStore = new List<object>();
            InitializeComponents();
            LoadBranches();
        }

        private void InitializeComponents()
        {
            Title = "Branch & Commit Viewer";
            Size = new Size(1200, 800);
            Padding = new Padding(10);

            var layout = new DynamicLayout();
            layout.DefaultSpacing = new Size(5, 5);

            InitializeSearchBox(layout);
            InitializeGrids(layout);
            InitializeButtons(layout);

            Content = layout;
        }

        private void InitializeSearchBox(DynamicLayout layout)
        {
            searchBox = new TextBox { PlaceholderText = "Search branches..." };
            searchBox.TextChanged += (sender, e) => {
                var searchText = searchBox.Text.ToLower();
                var branchData = GetBranchData()
                    .Where(b => b.Name.ToLower().Contains(searchText))
                    .ToList();
                branchGrid.DataStore = branchData;
            };
            layout.AddRow(searchBox);
        }

        private void InitializeGrids(DynamicLayout layout)
        {
            var splitter = new Splitter();
            splitter.Panel1 = CreateBranchSection();
            splitter.Panel2 = CreateCommitSection();
            splitter.Position = 400;

            layout.Add(splitter);
        }

        private Control CreateBranchSection()
        {
            var branchSection = new DynamicLayout();
            branchSection.DefaultPadding = new Padding(5);
            branchSection.Add(new Label { Text = "Branches" });

            branchGrid = new GridView
            {
                ShowHeader = true,
                AllowMultipleSelection = false,
                Height = 500
            };

            branchGrid.SelectionChanged += BranchGrid_SelectionChanged;

            branchGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Branch Name",
                DataCell = new TextBoxCell { Binding = Binding.Property<BranchData, string>(r => r.Name) },
                AutoSize = true
            });

            branchGrid.DataStore = GetBranchData();
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
                AllowMultipleSelection = true,
                Height = 500
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

            commitSection.Add(commitGrid, yscale: true);
            return commitSection;
        }

        private void InitializeButtons(DynamicLayout layout)
        {
            var buttonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5
            };

            var showDiffButton = new Button { Text = "Show Selected Commits" };
            showDiffButton.Click += ShowSelectedCommits_Click;
            buttonLayout.Items.Add(showDiffButton);

            var switchBranchButton = new Button { Text = "Switch to Branch" };
            switchBranchButton.Click += (sender, e) => {
                if (branchGrid.SelectedRow >= 0)
                {
                    var selectedBranch = (BranchData)branchGrid.SelectedItem;
                    ModelDiffCommand.Instance.SwitchBranch(selectedBranch.Name);
                    Close();
                }
            };
            buttonLayout.Items.Add(switchBranchButton);

            var refreshButton = new Button { Text = "Refresh" };
            refreshButton.Click += (sender, e) => {
                var visualizationObjects = doc.Objects
                    .Where(obj => obj.Attributes.GetUserString("CommitVisualization") == "true")
                    .Select(obj => obj.Id)
                    .ToList();

                foreach (var id in visualizationObjects)
                {
                    doc.Objects.Delete(id, true);
                }

                LoadBranches();
                commitGrid.DataStore = null;
                doc.Views.Redraw();
            };
            buttonLayout.Items.Add(refreshButton);

            layout.AddRow(buttonLayout);
        }

        private void BranchGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (branchGrid.SelectedRow >= 0)
            {
                var selectedBranch = (BranchData)branchGrid.SelectedItem;
                LoadCommitsForBranch(selectedBranch.Name);
                RhinoApp.WriteLine($"Selected branch: {selectedBranch.Name}");
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

        private void LoadBranches()
        {
            var branchData = GetBranchData();
            RhinoApp.WriteLine($"Loaded {branchData.Count} branches");
            branchGrid.DataStore = branchData;
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
                else
                {
                    RhinoApp.WriteLine("No valid commits selected");
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
