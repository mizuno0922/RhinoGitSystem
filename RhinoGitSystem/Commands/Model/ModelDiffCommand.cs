using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhinoGitSystem.Models;
using System.Xml.Linq;
using System.Xml;
using NewtonsoftFormatting = Newtonsoft.Json.Formatting;


namespace RhinoGitSystem.Commands.Model
{
    public class ModelDiffCommand : Command
    {
        private string _fileId;
        public string FileId
        {
            get
            {
                if (string.IsNullOrEmpty(_fileId))
                {
                    _fileId = GetFileId(Rhino.RhinoDoc.ActiveDoc);
                }
                return _fileId;
            }
            set { _fileId = value; }
        }

        private Dictionary<Guid, string> lastKnownState = new Dictionary<Guid, string>();
        private bool isInitialized = false;
        public string currentBranch = "main";
        public string fileId;

        public ModelDiffCommand()
        {
            Instance = this;
        }

        public static ModelDiffCommand Instance { get; private set; }

        public override string EnglishName => "ModelDiff";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!isInitialized || fileId != GetFileId(doc))
            {
                InitializeLastKnownState(doc);
            }

            string userMessage = string.Empty;
            var result = Rhino.Input.RhinoGet.GetString("Enter commit message", false, ref userMessage);
            if (result != Result.Success || string.IsNullOrEmpty(userMessage))
                return Result.Cancel;

            string userName = string.Empty;
            result = Rhino.Input.RhinoGet.GetString("Enter your name", false, ref userName);
            if (result != Result.Success || string.IsNullOrEmpty(userName))
                return Result.Cancel;

            SaveModelChanges(doc, userMessage, userName);
            return Result.Success;
        }

        private Dictionary<string, Dictionary<Guid, string>> branchStates = new Dictionary<string, Dictionary<Guid, string>>();

        private void InitializeLastKnownState(RhinoDoc doc)
        {
            fileId = GetFileId(doc);
            if (!branchStates.ContainsKey(currentBranch))
            {
                branchStates[currentBranch] = new Dictionary<Guid, string>();
            }
            var lastKnownState = branchStates[currentBranch];
            lastKnownState.Clear();
            foreach (var obj in doc.Objects)
            {
                if (obj != null && obj.Geometry != null)
                {
                    lastKnownState[obj.Id] = SerializeObject(obj);
                }
            }
            isInitialized = true;
            RhinoApp.WriteLine($"Initialized lastKnownState with {lastKnownState.Count} objects on branch '{currentBranch}' for file {fileId}.");
        }

        public string GetFileId(RhinoDoc doc)
        {
            if (string.IsNullOrEmpty(doc.Path))
            {
                return "unsaved_" + Guid.NewGuid().ToString("N");
            }
            return Path.GetFileNameWithoutExtension(doc.Path);
        }

        // Helper methods for model state management
        private void SaveModelChanges(RhinoDoc doc, string message, string author)
        {
            if (!branchStates.ContainsKey(currentBranch))
            {
                branchStates[currentBranch] = new Dictionary<Guid, string>();
            }
            var lastKnownState = branchStates[currentBranch];
            var changes = new List<ObjectChange>();
            var currentObjectIds = new HashSet<Guid>();

            foreach (var obj in doc.Objects)
            {
                if (obj == null || obj.Geometry == null) continue;

                currentObjectIds.Add(obj.Id);
                string currentState = SerializeObject(obj);

                if (!lastKnownState.TryGetValue(obj.Id, out string previousState))
                {
                    changes.Add(new ObjectChange
                    {
                        Id = obj.Id,
                        ChangeType = "Added",
                        SerializedGeometry = currentState,
                        Transform = GetObjectTransform(obj)
                    });
                }
                else if (currentState != previousState)
                {
                    changes.Add(new ObjectChange
                    {
                        Id = obj.Id,
                        ChangeType = "Modified",
                        SerializedGeometry = currentState,
                        Transform = GetObjectTransform(obj)
                    });
                }

                lastKnownState[obj.Id] = currentState;
            }

            var deletedObjects = lastKnownState.Keys.Except(currentObjectIds).ToList();
            foreach (var deletedId in deletedObjects)
            {
                changes.Add(new ObjectChange
                {
                    Id = deletedId,
                    ChangeType = "Deleted",
                    SerializedGeometry = lastKnownState[deletedId]
                });
                lastKnownState.Remove(deletedId);
            }

            string commitId = Guid.NewGuid().ToString("N");
            var modelState = new ModelState
            {
                CommitId = commitId,
                Changes = changes,
                Timestamp = DateTime.Now,
                Message = message,
                BranchName = currentBranch,
                ParentCommit = GetLastCommitHash(),
                Author = author
            };

            SaveModelState(modelState);

            RhinoApp.WriteLine($"Commit ID: {commitId}");
            RhinoApp.WriteLine($"Model state saved. File: {fileId}, Branch: '{currentBranch}', Message: {message}");
        }

        private string GetHistoryPath()
        {
            return Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"model_history_{FileId}.json");
        }

        public string SerializeObject(RhinoObject obj)
        {
            try
            {
                var geometry = obj.Geometry;
                var serializationOptions = new Rhino.FileIO.SerializationOptions();
                JObject jsonObject = new JObject();

                jsonObject["ObjectType"] = geometry.GetType().Name;
                jsonObject["Id"] = obj.Id.ToString();
                jsonObject["Layer"] = obj.Attributes.LayerIndex;

                var localGeometry = geometry.Duplicate();
                var bbox = geometry.GetBoundingBox(true);
                var localTransform = Transform.Translation(-new Vector3d(bbox.Min));
                localGeometry.Transform(localTransform);

                if (localGeometry is Brep brep)
                {
                    jsonObject["GeometryData"] = JObject.Parse(brep.ToJSON(serializationOptions));
                }
                else if (localGeometry is Curve curve)
                {
                    jsonObject["GeometryData"] = JObject.Parse(curve.ToJSON(serializationOptions));
                }
                else if (localGeometry is Surface surface)
                {
                    jsonObject["GeometryData"] = JObject.Parse(surface.ToJSON(serializationOptions));
                }
                else if (localGeometry is Mesh mesh)
                {
                    jsonObject["GeometryData"] = JObject.Parse(mesh.ToJSON(serializationOptions));
                }
                else if (localGeometry is SubD subd)
                {
                    jsonObject["GeometryData"] = JObject.Parse(subd.ToJSON(serializationOptions));
                }
                else if (localGeometry is Point point)
                {
                    jsonObject["GeometryData"] = JObject.FromObject(point.Location);
                }
                else if (localGeometry is PointCloud pointCloud)
                {
                    jsonObject["GeometryData"] = JObject.Parse(pointCloud.ToJSON(serializationOptions));
                }
                else
                {
                    jsonObject["GeometryData"] = JObject.Parse(localGeometry.ToJSON(serializationOptions));
                }

                jsonObject["Transform"] = new JArray { bbox.Min.X, bbox.Min.Y, bbox.Min.Z };

                return jsonObject.ToString(NewtonsoftFormatting.None);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error serializing object {obj.Id}: {ex.Message}");
                return null;
            }
        }

        public GeometryBase DeserializeObject(string serializedGeometry)
        {
            Point3d position;
            return DeserializeObject(serializedGeometry, out position);
        }

        public GeometryBase DeserializeObject(string serializedGeometry, out Point3d position)
        {
            try
            {
                var jObject = JObject.Parse(serializedGeometry);
                string objectType = jObject["ObjectType"].Value<string>();
                var geometryData = jObject["GeometryData"] as JObject;
                var transformArray = jObject["Transform"] as JArray;

                position = new Point3d(
                    transformArray[0].Value<double>(),
                    transformArray[1].Value<double>(),
                    transformArray[2].Value<double>()
                );

                GeometryBase geometry = null;
                switch (objectType)
                {
                    case "Box":
                    case "Extrusion":
                    case "Brep":
                        geometry = (GeometryBase)Brep.FromJSON(geometryData.ToString());
                        break;
                    case "Point":
                        var point = geometryData.ToObject<Point3d>();
                        geometry = new Point(point);
                        break;
                    case "PointCloud":
                        geometry = (GeometryBase)PointCloud.FromJSON(geometryData.ToString());
                        break;
                    case "Curve":
                    case "LineCurve":
                    case "ArcCurve":
                    case "PolylineCurve":
                    case "NurbsCurve":
                        geometry = (GeometryBase)Curve.FromJSON(geometryData.ToString());
                        break;
                    case "Surface":
                    case "NurbsSurface":
                        geometry = (GeometryBase)Surface.FromJSON(geometryData.ToString());
                        break;
                    case "Mesh":
                        geometry = (GeometryBase)Mesh.FromJSON(geometryData.ToString());
                        break;
                    case "SubD":
                        geometry = (GeometryBase)SubD.FromJSON(geometryData.ToString());
                        break;
                    default:
                        RhinoApp.WriteLine($"Unsupported geometry type: {objectType}");
                        return null;
                }

                if (geometry != null)
                {
                    geometry.Transform(Transform.Translation(new Vector3d(position)));
                }

                return geometry;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error deserializing object: {ex.Message}");
                position = Point3d.Origin;
                return null;
            }
        }

        public Transform GetObjectTransform(RhinoObject obj)
        {
            var bbox = obj.Geometry.GetBoundingBox(true);
            if (bbox.IsValid)
            {
                Vector3d translation = new Vector3d(bbox.Min);
                return Transform.Translation(translation);
            }
            return Transform.Identity;
        }

        public void SaveModelState(ModelState state)
        {
            var historyPath = GetHistoryPath();
            List<ModelState> history;

            try
            {
                if (File.Exists(historyPath))
                {
                    var json = File.ReadAllText(historyPath);
                    history = JsonConvert.DeserializeObject<List<ModelState>>(json);
                }
                else
                {
                    history = new List<ModelState>();
                }

                history.Add(state);
                File.WriteAllText(historyPath, JsonConvert.SerializeObject(history, NewtonsoftFormatting.Indented));
                UpdateBranchInfo(state);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error saving model state: {ex.Message}");
            }
        }

        public string GetLastCommitHash()
        {
            var historyPath = GetHistoryPath();
            if (File.Exists(historyPath))
            {
                var json = File.ReadAllText(historyPath);
                var history = JsonConvert.DeserializeObject<List<ModelState>>(json);
                if (history.Any())
                {
                    var lastCommit = history.LastOrDefault(c => c.BranchName == currentBranch);
                    if (lastCommit != null)
                    {
                        return lastCommit.CommitId;
                    }
                }
            }
            return string.Empty;
        }

        private void UpdateBranchInfo(ModelState state)
        {
            var branchesPath = GetBranchesPath();
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

            var branch = branches.FirstOrDefault(b => b.Name == state.BranchName);
            if (branch == null)
            {
                branch = new RhinoGitSystem.Models.Branch { Name = state.BranchName, Commits = new List<string>() };
                branches.Add(branch);
            }

            branch.Commits.Add(state.CommitId);
            File.WriteAllText(branchesPath, JsonConvert.SerializeObject(branches, NewtonsoftFormatting.Indented));
        }

        private string GetBranchesPath()
        {
            return Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"branches_{fileId}.json");
        }

        public List<RhinoGitSystem.Models.Branch> GetBranches(string fileId)
        {
            var branchesPath = Path.Combine(Rhino.ApplicationSettings.FileSettings.WorkingFolder, $"branches_{fileId}.json");
            if (File.Exists(branchesPath))
            {
                var json = File.ReadAllText(branchesPath);
                return JsonConvert.DeserializeObject<List<RhinoGitSystem.Models.Branch>>(json);
            }
            return new List<RhinoGitSystem.Models.Branch>();
        }

        public void SwitchBranch(string branchName)
        {
            if (string.IsNullOrEmpty(FileId))
            {
                RhinoApp.WriteLine("Error: FileId is not set. Cannot switch branch.");
                return;
            }

            if (currentBranch == branchName)
            {
                RhinoApp.WriteLine($"Already on branch '{branchName}' for file {FileId}.");
                return;
            }

            var historyPath = GetHistoryPath();
            if (!File.Exists(historyPath))
            {
                RhinoApp.WriteLine($"No model history found for file {FileId} at {historyPath}.");
                return;
            }

            if (branchStates.ContainsKey(currentBranch))
            {
                var currentState = new ModelState
                {
                    CommitId = Guid.NewGuid().ToString("N"),
                    Changes = branchStates[currentBranch].Select(kv => new ObjectChange
                    {
                        Id = kv.Key,
                        ChangeType = "Added",
                        SerializedGeometry = kv.Value,
                        Transform = Transform.Identity
                    }).ToList(),
                    Timestamp = DateTime.Now,
                    Message = $"Auto-save before switching to branch '{branchName}'",
                    BranchName = currentBranch,
                    ParentCommit = GetLastCommitHash(),
                    Author = "System"
                };
                SaveModelState(currentState);
            }

            currentBranch = branchName;
            if (!branchStates.ContainsKey(currentBranch))
            {
                branchStates[currentBranch] = new Dictionary<Guid, string>();
            }
            RhinoApp.WriteLine($"Switched to branch '{branchName}' for file {FileId}.");

            string latestCommit = GetLatestCommitForBranch(branchName);
            if (string.IsNullOrEmpty(latestCommit))
            {
                RhinoApp.WriteLine($"No commits found for branch '{branchName}'.");
                return;
            }

            ReconstructModel(Rhino.RhinoDoc.ActiveDoc, latestCommit);
        }

        public string GetLatestCommitForBranch(string branchName)
        {
            var history = GetModelHistory();
            var branchCommits = history.Where(s => s.BranchName == branchName).OrderByDescending(s => s.Timestamp);
            return branchCommits.FirstOrDefault()?.CommitId;
        }

        public List<ModelState> GetModelHistory()
        {
            var historyPath = GetHistoryPath();
            if (File.Exists(historyPath))
            {
                var json = File.ReadAllText(historyPath);
                return JsonConvert.DeserializeObject<List<ModelState>>(json);
            }
            return new List<ModelState>();
        }

        public ModelState GetModelStateByCommitHash(string commitHash)
        {
            var history = GetModelHistory();
            return history.FirstOrDefault(s => s.CommitId == commitHash);
        }

        public void SaveModelHistory(List<ModelState> history)
        {
            var historyPath = GetHistoryPath();
            File.WriteAllText(historyPath, JsonConvert.SerializeObject(history, NewtonsoftFormatting.Indented));
        }

        public void SaveBranches(List<RhinoGitSystem.Models.Branch> branches)
        {
            var branchesPath = GetBranchesPath();
            File.WriteAllText(branchesPath, JsonConvert.SerializeObject(branches, NewtonsoftFormatting.Indented));
        }

        public void UpdateLastKnownState(RhinoDoc doc)
        {
            if (!branchStates.ContainsKey(currentBranch))
            {
                branchStates[currentBranch] = new Dictionary<Guid, string>();
            }
            var lastKnownState = branchStates[currentBranch];
            lastKnownState.Clear();
            foreach (var obj in doc.Objects)
            {
                if (obj != null && obj.Geometry != null)
                {
                    lastKnownState[obj.Id] = SerializeObject(obj);
                }
            }
            RhinoApp.WriteLine($"Updated lastKnownState with {lastKnownState.Count} objects for branch '{currentBranch}'.");
        }

        public void ReconstructModel(RhinoDoc doc, string commitHash)
        {
            var historyPath = GetHistoryPath();
            if (!File.Exists(historyPath))
            {
                RhinoApp.WriteLine($"No model history found for file {fileId}.");
                return;
            }

            try
            {
                var json = File.ReadAllText(historyPath);
                var history = JsonConvert.DeserializeObject<List<ModelState>>(json);

                var targetState = history.FirstOrDefault(s => s.CommitId == commitHash);
                if (targetState == null)
                {
                    RhinoApp.WriteLine($"Commit '{commitHash}' not found for file {fileId}.");
                    return;
                }

                doc.Objects.Clear();
                if (!branchStates.ContainsKey(currentBranch))
                {
                    branchStates[currentBranch] = new Dictionary<Guid, string>();
                }
                branchStates[currentBranch].Clear();

                foreach (var change in targetState.Changes)
                {
                    if (change.ChangeType != "Deleted")
                    {
                        ApplyObjectChange(doc, change);
                        branchStates[currentBranch][change.Id] = change.SerializedGeometry;
                    }
                }

                doc.Views.Redraw();
                RhinoApp.WriteLine($"Model reconstructed to commit '{commitHash}' on branch '{targetState.BranchName}' for file {fileId}.");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error reconstructing model: {ex.Message}");
            }
        }

        public void ApplyObjectChange(RhinoDoc doc, ObjectChange change)
        {
            if (change.ChangeType == "Deleted")
            {
                doc.Objects.Delete(change.Id, quiet: true);
            }
            else // Added or Modified
            {
                var obj = DeserializeObject(change.SerializedGeometry);
                if (obj != null)
                {
                    var attributes = new ObjectAttributes();
                    attributes.ObjectId = change.Id;
                    doc.Objects.Add(obj, attributes);
                }
            }
        }

        private void ApplyModelState(RhinoDoc doc, ModelState state)
        {
            foreach (var change in state.Changes)
            {
                switch (change.ChangeType)
                {
                    case "Added":
                    case "Modified":
                        var obj = DeserializeObject(change.SerializedGeometry);
                        if (obj != null)
                        {
                            if (change.Transform != null)
                            {
                                obj.Transform(change.Transform);
                            }
                            doc.Objects.Add(obj);
                        }
                        break;
                    case "Deleted":
                        doc.Objects.Delete(change.Id, quiet: true);
                        break;
                }
            }
        }
    }
}