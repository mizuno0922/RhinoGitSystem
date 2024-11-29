using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ipfs.Http;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Sync.Pull
{
    public class PullBranchJsonCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;

        public PullBranchJsonCommand()
        {
            Instance = this;
        }

        public static PullBranchJsonCommand Instance { get; private set; }

        public override string EnglishName => "PullBranchJson";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string inputPath = string.Empty;
            var result = Rhino.Input.RhinoGet.GetString("Enter input file path", false, ref inputPath);
            if (result != Result.Success || string.IsNullOrEmpty(inputPath))
                return Result.Cancel;

            PullBranch(doc, inputPath);
            return Result.Success;
        }

        private void PullBranch(RhinoDoc doc, string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                RhinoApp.WriteLine($"File not found: {inputPath}");
                return;
            }

            var pullBranchHistory = JsonConvert.DeserializeObject<List<ModelState>>(File.ReadAllText(inputPath));
            if (pullBranchHistory == null || !pullBranchHistory.Any())
            {
                RhinoApp.WriteLine("No valid history found in the input file");
                return;
            }

            var branchName = pullBranchHistory.First().BranchName;

            // Update model history
            UpdateModelHistory(pullBranchHistory);

            // Update branch information
            UpdateBranchInfo(branchName, pullBranchHistory);

            RhinoApp.WriteLine($"Branch '{branchName}' history pulled and merged");
        }

        private void UpdateModelHistory(List<ModelState> pullBranchHistory)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var updatedHistory = new List<ModelState>(modelHistory);
            var processedCommits = new HashSet<string>();

            foreach (var pullState in pullBranchHistory)
            {
                if (processedCommits.Contains(pullState.CommitId))
                    continue;

                var existingState = updatedHistory.FirstOrDefault(s => s.CommitId == pullState.CommitId);
                if (existingState == null)
                {
                    // Add new state
                    AddNewCommitWithParentCheck(updatedHistory, pullState);
                    RhinoApp.WriteLine($"Added new commit: {pullState.CommitId}");
                }
                else
                {
                    // Update existing state if necessary
                    if (!ModelStatesAreEqual(existingState, pullState))
                    {
                        UpdateExistingCommit(existingState, pullState);
                        RhinoApp.WriteLine($"Updated existing commit: {pullState.CommitId}");
                    }
                }

                processedCommits.Add(pullState.CommitId);
            }

            // Sort the history by timestamp
            updatedHistory = updatedHistory.OrderBy(s => s.Timestamp).ToList();

            // Rebuild the parent-child relationships
            RebuildParentChildRelationships(updatedHistory);

            // Save the updated history
            ModelDiffCommand.Instance.SaveModelHistory(updatedHistory);
        }

        private void AddNewCommitWithParentCheck(List<ModelState> history, ModelState newState)
        {
            var parentCommit = history.FirstOrDefault(s => s.CommitId == newState.ParentCommit);
            if (parentCommit == null)
            {
                // If parent is not found, try to find a suitable parent
                parentCommit = FindSuitableParent(history, newState);
                if (parentCommit != null)
                {
                    newState.ParentCommit = parentCommit.CommitId;
                    RhinoApp.WriteLine($"Adjusted parent commit for {newState.CommitId} to {parentCommit.CommitId}");
                }
                else
                {
                    RhinoApp.WriteLine($"Warning: No suitable parent found for commit {newState.CommitId}");
                }
            }

            history.Add(newState);
        }

        private ModelState FindSuitableParent(List<ModelState> history, ModelState newState)
        {
            // Find the latest commit that occurred before the new state
            return history.Where(s => s.Timestamp < newState.Timestamp)
                         .OrderByDescending(s => s.Timestamp)
                         .FirstOrDefault();
        }

        private void UpdateExistingCommit(ModelState existingState, ModelState pullState)
        {
            existingState.Changes = pullState.Changes;
            existingState.Timestamp = pullState.Timestamp;
            existingState.Message = pullState.Message;
            existingState.Author = pullState.Author;
            existingState.ParentCommit = pullState.ParentCommit;
        }

        private void RebuildParentChildRelationships(List<ModelState> history)
        {
            for (int i = 1; i < history.Count; i++)
            {
                history[i].ParentCommit = history[i - 1].CommitId;
            }
        }

        private bool ModelStatesAreEqual(ModelState state1, ModelState state2)
        {
            return state1.CommitId == state2.CommitId &&
                   state1.Timestamp == state2.Timestamp &&
                   state1.Message == state2.Message &&
                   state1.Author == state2.Author &&
                   state1.ParentCommit == state2.ParentCommit &&
                   state1.Changes.Count == state2.Changes.Count;
        }

        private void UpdateBranchInfo(string branchName, List<ModelState> pullBranchHistory)
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            var branch = branches.FirstOrDefault(b => b.Name == branchName);
            if (branch == null)
            {
                branch = new RhinoGitSystem.Models.Branch { Name = branchName, Commits = new List<string>() };
                branches.Add(branch);
                RhinoApp.WriteLine($"Created new branch: {branchName}");
            }

            var newCommits = pullBranchHistory.Select(s => s.CommitId).Except(branch.Commits).ToList();
            branch.Commits.AddRange(newCommits);
            branch.Commits = branch.Commits.Distinct()
                .OrderBy(c => pullBranchHistory.FindIndex(s => s.CommitId == c))
                .ToList();

            ModelDiffCommand.Instance.SaveBranches(branches);
            RhinoApp.WriteLine($"Updated branch '{branchName}' with {newCommits.Count()} new commits");
        }
    }
}
