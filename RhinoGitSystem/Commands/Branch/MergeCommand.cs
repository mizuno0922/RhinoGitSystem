using Rhino.Commands;
using Rhino;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhinoGitSystem.Commands.Branch
{
    public class MergeCommand : Command
    {
        public MergeCommand()
        {
            Instance = this;
        }

        public static MergeCommand Instance { get; private set; }

        public override string EnglishName => "MergeBranch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string sourceBranch = string.Empty;
            var result = Rhino.Input.RhinoGet.GetString("Enter source branch name to merge", false, ref sourceBranch);
            if (result != Result.Success || string.IsNullOrEmpty(sourceBranch))
                return Result.Cancel;

            string userName = string.Empty;
            result = Rhino.Input.RhinoGet.GetString("Enter your name for the merge commit", false, ref userName);
            if (result != Result.Success || string.IsNullOrEmpty(userName))
                return Result.Cancel;

            MergeBranches(doc, sourceBranch, ModelDiffCommand.Instance.currentBranch, userName);
            return Result.Success;
        }

        private void MergeBranches(RhinoDoc doc, string sourceBranch, string targetBranch, string author)
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.fileId);
            var sourceBranchObj = branches.FirstOrDefault(b => b.Name == sourceBranch);
            var targetBranchObj = branches.FirstOrDefault(b => b.Name == targetBranch);

            if (sourceBranchObj == null || targetBranchObj == null)
            {
                RhinoApp.WriteLine("One or both branches not found.");
                return;
            }

            var branchPoint = FindBranchPoint(sourceBranchObj);
            if (branchPoint == null)
            {
                RhinoApp.WriteLine("Branch point not found. Cannot merge.");
                return;
            }

            RhinoApp.WriteLine($"Branch point found: {branchPoint.CommitId}");

            var sourceChanges = GetChangesSinceBranchPoint(sourceBranch, branchPoint.CommitId);
            var targetChanges = GetChangesSinceBranchPoint(targetBranch, branchPoint.CommitId);

            RhinoApp.WriteLine($"Source changes: {sourceChanges.Count}, Target changes: {targetChanges.Count}");

            var mergedChanges = MergeChanges(sourceChanges, targetChanges);
            if (HasConflicts(mergedChanges))
            {
                RhinoApp.WriteLine("Conflicts detected. Please resolve conflicts manually.");
                return;
            }

            mergedChanges = RemoveDuplicateObjects(mergedChanges);

            ApplyMergedChanges(doc, mergedChanges);

            ModelDiffCommand.Instance.UpdateLastKnownState(doc);

            string commitId = Guid.NewGuid().ToString("N");
            var mergeState = new ModelState
            {
                CommitId = commitId,
                Changes = mergedChanges,
                Timestamp = DateTime.Now,
                Message = $"Merge branch '{sourceBranch}' into '{targetBranch}'",
                BranchName = targetBranch,
                ParentCommit = ModelDiffCommand.Instance.GetLastCommitHash(),
                Author = author
            };

            ModelDiffCommand.Instance.SaveModelState(mergeState);

            // Update branch information
            branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            targetBranchObj = branches.FirstOrDefault(b => b.Name == targetBranch);
            if (targetBranchObj != null)
            {
                targetBranchObj.Commits.Add(commitId);
                ModelDiffCommand.Instance.SaveBranches(branches);
            }

            RhinoApp.WriteLine($"Merge completed. New commit: {commitId}");
        }

        private List<ObjectChange> RemoveDuplicateObjects(List<ObjectChange> changes)
        {
            var uniqueChanges = new List<ObjectChange>();
            var seenGeometries = new Dictionary<string, ObjectChange>();

            foreach (var change in changes)
            {
                if (change.ChangeType == "Deleted")
                {
                    uniqueChanges.Add(change);
                    continue;
                }

                var geometryHash = ComputeGeometryHash(change.SerializedGeometry);
                if (!seenGeometries.ContainsKey(geometryHash))
                {
                    seenGeometries[geometryHash] = change;
                    uniqueChanges.Add(change);
                }
                else
                {
                    RhinoApp.WriteLine($"Duplicate object found and removed: {change.Id}");
                }
            }

            RhinoApp.WriteLine($"Removed {changes.Count - uniqueChanges.Count} duplicate objects");
            return uniqueChanges;
        }

        private string ComputeGeometryHash(string serializedGeometry)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(serializedGeometry));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private ModelState FindBranchPoint(RhinoGitSystem.Models.Branch branch)
        {
            var history = ModelDiffCommand.Instance.GetModelHistory();
            var firstBranchCommit = history.FirstOrDefault(s => s.CommitId == branch.Commits.First());

            if (firstBranchCommit != null && !string.IsNullOrEmpty(firstBranchCommit.ParentCommit))
            {
                return history.FirstOrDefault(s => s.CommitId == firstBranchCommit.ParentCommit);
            }

            return null;
        }

        private List<ObjectChange> GetChangesSinceBranchPoint(string branchName, string branchPointCommitId)
        {
            var history = ModelDiffCommand.Instance.GetModelHistory();
            var changes = new List<ObjectChange>();
            var branchPointTimestamp = history.First(s => s.CommitId == branchPointCommitId).Timestamp;
            var branchCommits = history.Where(s => s.BranchName == branchName && s.Timestamp > branchPointTimestamp);

            foreach (var commit in branchCommits)
            {
                changes.AddRange(commit.Changes);
            }

            return changes;
        }

        private List<ObjectChange> MergeChanges(List<ObjectChange> sourceChanges, List<ObjectChange> targetChanges)
        {
            var mergedChanges = new List<ObjectChange>(targetChanges);
            var processedIds = new HashSet<Guid>();

            foreach (var sourceChange in sourceChanges)
            {
                if (processedIds.Contains(sourceChange.Id))
                    continue;

                var existingChange = mergedChanges.FirstOrDefault(c => c.Id == sourceChange.Id);
                if (existingChange != null)
                {
                    mergedChanges.Remove(existingChange);
                    if (sourceChange.ChangeType != "Deleted")
                    {
                        mergedChanges.Add(sourceChange);
                    }
                }
                else
                {
                    mergedChanges.Add(sourceChange);
                }

                processedIds.Add(sourceChange.Id);
            }

            var deletedIds = sourceChanges.Where(c => c.ChangeType == "Deleted").Select(c => c.Id).ToHashSet();
            mergedChanges.RemoveAll(c => deletedIds.Contains(c.Id));

            return mergedChanges;
        }

        private bool HasConflicts(List<ObjectChange> mergedChanges)
        {
            // 競合検出のロジックを実装
            return false;
        }

        private void ApplyMergedChanges(RhinoDoc doc, List<ObjectChange> mergedChanges)
        {
            doc.Objects.Clear();

            foreach (var change in mergedChanges)
            {
                if (change.ChangeType != "Deleted")
                {
                    ModelDiffCommand.Instance.ApplyObjectChange(doc, change);
                }
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine($"Applied {mergedChanges.Count} changes. Deleted: {mergedChanges.Count(c => c.ChangeType == "Deleted")}");
        }

        private List<RhinoGitSystem.Models.Branch> GetBranches(string fileId)
        {
            return ModelDiffCommand.Instance.GetBranches(fileId);
        }
    }
}
