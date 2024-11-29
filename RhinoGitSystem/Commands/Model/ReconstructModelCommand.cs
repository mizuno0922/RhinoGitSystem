using System;
using Rhino;
using Rhino.Commands;

namespace RhinoGitSystem.Commands.Model
{
    public class ReconstructModelCommand : Command
    {
        public ReconstructModelCommand()
        {
            Instance = this;
        }

        public static ReconstructModelCommand Instance { get; private set; }

        public override string EnglishName => "ReconstructModel";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string commitHash = string.Empty;
            var result = Rhino.Input.RhinoGet.GetString("Enter commit hash to reconstruct", false, ref commitHash);
            if (result != Result.Success || string.IsNullOrEmpty(commitHash))
                return Result.Cancel;

            ModelDiffCommand.Instance.ReconstructModel(doc, commitHash);
            return Result.Success;
        }
    }

}