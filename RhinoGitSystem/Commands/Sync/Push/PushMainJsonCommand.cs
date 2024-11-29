using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using RhinoGitSystem.Commands.Model;

namespace RhinoGitSystem.Commands.Sync.Push
{
    public class PushMainJsonCommand : Command
    {
        public PushMainJsonCommand()
        {
            Instance = this;
        }

        public static PushMainJsonCommand Instance { get; private set; }

        public override string EnglishName => "PushMainJson";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            PushMain(doc);
            return Result.Success;
        }

        private void PushMain(RhinoDoc doc)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var mainHistory = modelHistory.Where(s => s.BranchName == "main").ToList();

            if (!mainHistory.Any())
            {
                RhinoApp.WriteLine("No history found for main branch");
                return;
            }

            string outputPath = GetOutputPath(doc, "main");
            File.WriteAllText(outputPath, JsonConvert.SerializeObject(mainHistory, Formatting.Indented));
            RhinoApp.WriteLine($"Main branch history pushed to {outputPath}");
        }

        private string GetOutputPath(RhinoDoc doc, string branchName)
        {
            string directory = Path.GetDirectoryName(doc.Path);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Rhino.ApplicationSettings.FileSettings.WorkingFolder;
            }
            string fileName = $"{branchName}_history.json";
            return Path.Combine(directory, fileName);
        }
    }
}
