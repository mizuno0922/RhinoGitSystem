using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Rhino;
using Rhino.Commands;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Model
{
    public class ViewHistoryCommand : Command
    {
        public ViewHistoryCommand()
        {
            Instance = this;
        }

        public static ViewHistoryCommand Instance { get; private set; }

        public override string EnglishName => "ViewModelHistory";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string fileId = ModelDiffCommand.Instance.GetFileId(doc);
            var historyPath = Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"model_history_{fileId}.json");
            if (!File.Exists(historyPath))
            {
                RhinoApp.WriteLine($"No model history found for file {fileId}.");
                return Result.Nothing;
            }

            try
            {
                var json = File.ReadAllText(historyPath);
                var history = JsonConvert.DeserializeObject<List<ModelState>>(json);

                foreach (var state in history)
                {
                    RhinoApp.WriteLine($"{state.Timestamp:yyyy-MM-dd HH:mm:ss} - Branch: {state.BranchName} - {state.Message}");
                    RhinoApp.WriteLine($"Commit: {state.CommitId}");
                    RhinoApp.WriteLine($"Author: {state.Author}");
                    RhinoApp.WriteLine($"Parent: {state.ParentCommit}");
                    RhinoApp.WriteLine($"Changes: {state.Changes.Count}");
                    foreach (var change in state.Changes)
                    {
                        RhinoApp.WriteLine($"  {change.ChangeType}: {change.Id}");
                    }
                    RhinoApp.WriteLine("------------------------");
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error reading model history: {ex.Message}");
                return Result.Failure;
            }

            return Result.Success;
        }
    }
}