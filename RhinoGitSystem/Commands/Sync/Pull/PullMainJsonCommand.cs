using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Sync.Pull
{
    public class PullMainJsonCommand : Command
    {
        public PullMainJsonCommand()
        {
            Instance = this;
        }

        public static PullMainJsonCommand Instance { get; private set; }

        public override string EnglishName => "PullMainJson";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string inputPath = string.Empty;

            var result = Rhino.Input.RhinoGet.GetString("Enter input file path", false, ref inputPath);
            if (result != Result.Success || string.IsNullOrEmpty(inputPath))
                return Result.Cancel;

            PullMain(doc, inputPath);

            return Result.Success;
        }

        private void PullMain(RhinoDoc doc, string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                RhinoApp.WriteLine($"File not found: {inputPath}");
                return;
            }

            var pullMainHistory = JsonConvert.DeserializeObject<List<ModelState>>(File.ReadAllText(inputPath));
            if (pullMainHistory == null || !pullMainHistory.Any())
            {
                RhinoApp.WriteLine("No valid history found in the input file");
                return;
            }

            // Replace the entire main branch history with the pulled history
            UpdateModelHistory(pullMainHistory);

            // Update branch information
            UpdateBranchInfo("main", pullMainHistory);

            // Reconstruct the model to the latest state of the pulled main branch
            var latestMainCommit = pullMainHistory.LastOrDefault()?.CommitId;
            if (!string.IsNullOrEmpty(latestMainCommit))
            {
                ModelDiffCommand.Instance.ReconstructModel(doc, latestMainCommit);
            }

            RhinoApp.WriteLine($"Main branch history pulled and model updated from {inputPath}");
        }

        private List<ModelState> MergeMainHistories(List<ModelState> currentHistory, List<ModelState> pulledHistory)
        {
            var mergedHistory = new List<ModelState>();
            var allCommits = currentHistory.Concat(pulledHistory)
                .OrderBy(s => s.Timestamp)
                .ToList();

            foreach (var state in allCommits)
            {
                if (!mergedHistory.Any(s => s.CommitId == state.CommitId))
                {
                    mergedHistory.Add(state);
                }
            }

            return mergedHistory;
        }

        private void UpdateModelHistory(List<ModelState> pulledHistory)
        {
            var fullHistory = ModelDiffCommand.Instance.GetModelHistory();

            // Remove all existing main branch commits
            fullHistory.RemoveAll(s => s.BranchName == "main");

            // Add pulled main branch history
            fullHistory.AddRange(pulledHistory);

            // Sort the history by timestamp
            fullHistory = fullHistory.OrderBy(s => s.Timestamp).ToList();

            // Save the updated history
            ModelDiffCommand.Instance.SaveModelHistory(fullHistory);
        }

        private void UpdateBranchInfo(string branchName, List<ModelState> branchHistory)
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            var branch = branches.FirstOrDefault(b => b.Name == branchName);
            if (branch == null)
            {
                branch = new RhinoGitSystem.Models.Branch { Name = branchName, Commits = new List<string>() };
                branches.Add(branch);
            }

            branch.Commits = branchHistory.Select(s => s.CommitId).ToList();
            ModelDiffCommand.Instance.SaveBranches(branches);
        }
    }
}
