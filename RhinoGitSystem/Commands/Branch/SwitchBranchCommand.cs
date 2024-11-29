using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Branch
{
    public class SwitchBranchCommand : Command
    {
        public SwitchBranchCommand()
        {
            Instance = this;
        }

        public static SwitchBranchCommand Instance { get; private set; }

        public override string EnglishName => "SwitchBranch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ModelDiffCommand.Instance.FileId = ModelDiffCommand.Instance.GetFileId(doc);
            string fileId = ModelDiffCommand.Instance.FileId;
            RhinoApp.WriteLine($"Current FileId: {fileId}");

            var branches = GetBranches(fileId);
            if (branches == null || !branches.Any())
            {
                RhinoApp.WriteLine($"No branches found for file {fileId}.");
                return Result.Failure;
            }

            string branchName = string.Empty;
            var options = new GetOption();
            foreach (var branch in branches)
            {
                options.AddOption(branch.Name);
            }

            var result = options.Get();
            if (result == GetResult.Option)
            {
                branchName = options.Option().EnglishName;
                SwitchToNewBranch(doc, branchName, fileId);
                return Result.Success;
            }

            return Result.Cancel;
        }

        private List<RhinoGitSystem.Models.Branch> GetBranches(string fileId)
        {
            var branchesPath = Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"branches_{fileId}.json");
            if (File.Exists(branchesPath))
            {
                var json = File.ReadAllText(branchesPath);
                return JsonConvert.DeserializeObject<List<RhinoGitSystem.Models.Branch>>(json);
            }
            return null;
        }

        private void SwitchToNewBranch(RhinoDoc doc, string branchName, string fileId)
        {
            var branches = GetBranches(fileId);
            var targetBranch = branches.FirstOrDefault(b => b.Name == branchName);

            if (targetBranch == null)
            {
                RhinoApp.WriteLine($"Branch '{branchName}' not found for file {fileId}.");
                return;
            }

            if (!targetBranch.Commits.Any())
            {
                RhinoApp.WriteLine($"Creating initial commit for branch '{branchName}'.");
                CreateFirstCommit(doc, branchName, fileId);
            }
            else
            {
                ModelDiffCommand.Instance.SwitchBranch(branchName);
            }
        }

        private void CreateFirstCommit(RhinoDoc doc, string branchName, string fileId)
        {
            var parentCommit = ModelDiffCommand.Instance.GetLastCommitHash();
            var parentState = ModelDiffCommand.Instance.GetModelStateByCommitHash(parentCommit);

            var changes = new List<ObjectChange>();
            if (parentState != null)
            {
                changes = parentState.Changes.Select(c => new ObjectChange
                {
                    Id = c.Id,
                    ChangeType = c.ChangeType,
                    SerializedGeometry = c.SerializedGeometry,
                    Transform = c.Transform
                }).ToList();
            }
            else
            {
                foreach (var obj in doc.Objects)
                {
                    if (obj != null && obj.Geometry != null)
                    {
                        changes.Add(new ObjectChange
                        {
                            Id = obj.Id,
                            ChangeType = "Added",
                            SerializedGeometry = ModelDiffCommand.Instance.SerializeObject(obj),
                            Transform = ModelDiffCommand.Instance.GetObjectTransform(obj)
                        });
                    }
                }
            }

            string commitId = Guid.NewGuid().ToString("N");
            var modelState = new ModelState
            {
                CommitId = commitId,
                Changes = changes,
                Timestamp = DateTime.Now,
                Message = $"Initial commit for branch '{branchName}'",
                BranchName = branchName,
                ParentCommit = parentCommit,
                Author = "System"
            };

            ModelDiffCommand.Instance.SaveModelState(modelState);
            ModelDiffCommand.Instance.SwitchBranch(branchName);

            RhinoApp.WriteLine($"Created initial commit for branch '{branchName}'. Commit ID: {commitId}");
        }
    }
}