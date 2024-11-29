using System;
using System.IO;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Newtonsoft.Json;
using System.Linq;
using NewtonsoftFormatting = Newtonsoft.Json.Formatting;

namespace RhinoGitSystem.Commands.Model
{
    public class BranchCommand : Command
    {
        public BranchCommand()
        {
            Instance = this;
        }

        public static BranchCommand Instance { get; private set; }

        public override string EnglishName => "Branch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string branchName = string.Empty;
            var result = Rhino.Input.RhinoGet.GetString("Enter new branch name", false, ref branchName);
            if (result != Result.Success || string.IsNullOrEmpty(branchName))
                return Result.Cancel;

            CreateBranch(doc, branchName);
            return Result.Success;
        }

        private void CreateBranch(RhinoDoc doc, string branchName)
        {
            string fileId = ModelDiffCommand.Instance.GetFileId(doc);
            var branchesPath = Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"branches_{fileId}.json");
            List<RhinoGitSystem.Models.Branch> branches;

            if (File.Exists(branchesPath))
            {
                var json = File.ReadAllText(branchesPath);
                branches = JsonConvert.DeserializeObject<List<RhinoGitSystem.Models.Branch>>(json);
            }
            else
            {
                branches = new List<RhinoGitSystem.Models.Branch>();
            }

            if (branches.Any(b => b.Name == branchName))
            {
                RhinoApp.WriteLine($"Branch '{branchName}' already exists for file {fileId}.");
                return;
            }

            var newBranch = new RhinoGitSystem.Models.Branch { Name = branchName, Commits = new List<string>() };
            branches.Add(newBranch);

            File.WriteAllText(branchesPath, JsonConvert.SerializeObject(branches, Formatting.Indented));
            RhinoApp.WriteLine($"Created new branch: {branchName} for file {fileId}");
        }
    }
}