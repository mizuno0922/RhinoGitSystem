using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoGitSystem.Commands.Model;

namespace RhinoGitSystem.Commands.Visualization
{
    public class ShowBranchCommand : Command
    {
        public ShowBranchCommand()
        {
            Instance = this;
        }

        public static ShowBranchCommand Instance { get; private set; }

        public override string EnglishName => "ShowBranch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            if (branches == null || !branches.Any())
            {
                RhinoApp.WriteLine("No branches found.");
                return Result.Nothing;
            }

            var go = new GetOption();
            go.SetCommandPrompt("Select branch to view");
            foreach (var branch in branches)
            {
                if (branch.Name != "main") // メインブランチ以外を表示
                {
                    go.AddOption(branch.Name);
                }
            }

            var result = go.Get();
            if (result == GetResult.Option)
            {
                string selectedBranch = go.Option().EnglishName;
                ShowBranchDiff(doc, selectedBranch);
                return Result.Success;
            }

            return Result.Cancel;
        }

        private void ShowBranchDiff(RhinoDoc doc, string branchName)
        {
            var currentState = GetCurrentState(doc);

            // メインブランチの最新状態を取得
            var mainHistory = ModelDiffCommand.Instance.GetModelHistory()
                .Where(s => s.BranchName == "main")
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();

            // 選択されたブランチの最新状態を取得
            var branchHistory = ModelDiffCommand.Instance.GetModelHistory()
                .Where(s => s.BranchName == branchName)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();

            if (mainHistory == null || branchHistory == null)
            {
                RhinoApp.WriteLine("Unable to compare branch states.");
                return;
            }

            // ドキュメントをクリア
            doc.Objects.Clear();

            // メインブランチの状態を基本色で表示
            foreach (var change in mainHistory.Changes)
            {
                if (change.ChangeType != "Deleted")
                {
                    var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                    if (obj != null)
                    {
                        var attributes = new ObjectAttributes();
                        attributes.ColorSource = ObjectColorSource.ColorFromLayer;
                        doc.Objects.Add(obj, attributes);
                    }
                }
            }

            // ブランチの変更を色付きで表示
            foreach (var change in branchHistory.Changes)
            {
                var existingChange = mainHistory.Changes.FirstOrDefault(c => c.Id == change.Id);

                if (existingChange == null && change.ChangeType != "Deleted")
                {
                    // 新規追加されたオブジェクト（緑）
                    var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                    if (obj != null)
                    {
                        var attributes = new ObjectAttributes();
                        attributes.ColorSource = ObjectColorSource.ColorFromObject;
                        attributes.ObjectColor = System.Drawing.Color.Green;
                        doc.Objects.Add(obj, attributes);
                    }
                }
                else if (existingChange != null && change.SerializedGeometry != existingChange.SerializedGeometry)
                {
                    // 変更されたオブジェクト（黄）
                    var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                    if (obj != null)
                    {
                        var attributes = new ObjectAttributes();
                        attributes.ColorSource = ObjectColorSource.ColorFromObject;
                        attributes.ObjectColor = System.Drawing.Color.Yellow;
                        doc.Objects.Add(obj, attributes);
                    }
                }
            }

            // mainには存在するがブランチで削除されたオブジェクトを半透明の赤で表示
            foreach (var mainChange in mainHistory.Changes)
            {
                var branchChange = branchHistory.Changes.FirstOrDefault(c => c.Id == mainChange.Id);
                if (branchChange == null || branchChange.ChangeType == "Deleted")
                {
                    var obj = ModelDiffCommand.Instance.DeserializeObject(mainChange.SerializedGeometry);
                    if (obj != null)
                    {
                        var attributes = new ObjectAttributes();
                        attributes.ColorSource = ObjectColorSource.ColorFromObject;
                        attributes.ObjectColor = System.Drawing.Color.FromArgb(128, System.Drawing.Color.Red);
                        doc.Objects.Add(obj, attributes);
                    }
                }
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine($"Showing diff between main and {branchName}");
            RhinoApp.WriteLine("Green: Added objects");
            RhinoApp.WriteLine("Yellow: Modified objects");
            RhinoApp.WriteLine("Red (transparent): Deleted objects");
            RhinoApp.WriteLine("Default color: Unchanged objects");
        }

        private Dictionary<Guid, string> GetCurrentState(RhinoDoc doc)
        {
            var state = new Dictionary<Guid, string>();
            foreach (var obj in doc.Objects)
            {
                if (obj != null && obj.Geometry != null)
                {
                    state[obj.Id] = ModelDiffCommand.Instance.SerializeObject(obj);
                }
            }
            return state;
        }
    }
}
