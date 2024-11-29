using System.IO;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using System.Linq;
using Rhino.Input;
using RhinoGitSystem.Commands.Model;

namespace RhinoGitSystem.Commands.Sync.Push
{
    public class PushBranchJsonCommand : Command
    {
        public PushBranchJsonCommand()
        {
            Instance = this;
        }

        public static PushBranchJsonCommand Instance { get; private set; }

        public override string EnglishName => "PushBranchJson";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string branchName = string.Empty;
            var result = RhinoGet.GetString("Enter branch name to push", false, ref branchName);
            if (result != Result.Success || string.IsNullOrEmpty(branchName))
                return Result.Cancel;

            PushBranchJson(doc, branchName);
            return Result.Success;
        }

        private void PushBranchJson(RhinoDoc doc, string branchName)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var branchHistory = modelHistory.Where(s => s.BranchName == branchName).ToList();

            if (!branchHistory.Any())
            {
                RhinoApp.WriteLine($"No history found for branch '{branchName}'");
                return;
            }

            string jsonContent = JsonConvert.SerializeObject(branchHistory, Formatting.Indented);
            string outputPath = GetOutputPath(doc, branchName);
            File.WriteAllText(outputPath, jsonContent);

            RhinoApp.WriteLine($"Branch history exported to: {outputPath}");
        }

        private string GetOutputPath(RhinoDoc doc, string branchName)
        {
            string baseDirectory = Path.GetDirectoryName(doc.Path);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = Rhino.ApplicationSettings.FileSettings.WorkingFolder;
            }
            string fileName = $"{branchName}.json";
            return Path.Combine(baseDirectory, fileName);
        }
    }
}
