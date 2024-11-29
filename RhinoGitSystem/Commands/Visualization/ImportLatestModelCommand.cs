using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ipfs.Http;
using Nethereum.Web3;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using System.Linq;
using Newtonsoft.Json;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Config;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Visualization
{
    public class ImportLatestModelCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;
        private List<Guid> importedObjectIds = new List<Guid>();
        private List<Guid> generatedMake2DObjectIds = new List<Guid>();
        private List<Guid> rotatedObjectIds = new List<Guid>();

        public ImportLatestModelCommand()
        {
            Instance = this;
            string localIpAddress = "192.168.0.144";
            ipfs = new IpfsClient($"http://{localIpAddress}:5001");
            ganacheUrl = $"http://localhost:7545";
        }

        public static ImportLatestModelCommand Instance { get; private set; }

        public override string EnglishName => "ImportLatestModel";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string tokenIdInput = GetStringInput("Enter NFT Token ID");
            if (string.IsNullOrEmpty(tokenIdInput) || !BigInteger.TryParse(tokenIdInput, out BigInteger tokenId))
            {
                RhinoApp.WriteLine("Invalid Token ID");
                return Result.Failure;
            }

            double pitch = GetDoubleInput("Enter pitch for layout", 1000);

            Task.Run(async () =>
            {
                try
                {
                    var branchPath = await GetBranchPathFromNFT(tokenId);
                    if (string.IsNullOrEmpty(branchPath))
                    {
                        RhinoApp.WriteLine($"Branch path not found for Token ID: {tokenId}");
                        return;
                    }

                    importedObjectIds.Clear();
                    rotatedObjectIds.Clear();
                    ImportLatestModel(doc, branchPath);
                    GenerateAndLayoutMake2D(doc, pitch);
                    RemoveAllModelObjects(doc);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Error: {ex.Message}");
                }
            }).Wait();

            return Result.Success;
        }

        private string GetStringInput(string prompt, string defaultValue = "")
        {
            GetString gs = new GetString();
            gs.SetCommandPrompt(prompt);
            if (!string.IsNullOrEmpty(defaultValue))
                gs.SetDefaultString(defaultValue);

            if (gs.Get() != GetResult.String)
                return defaultValue;

            return gs.StringResult();
        }

        private double GetDoubleInput(string prompt, double defaultValue)
        {
            var gi = new GetNumber();
            gi.SetCommandPrompt(prompt);
            gi.SetDefaultNumber(defaultValue);

            if (gi.Get() != GetResult.Number)
                return defaultValue;

            return gi.Number();
        }

        private async Task<string> GetBranchPathFromNFT(BigInteger tokenId)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            string abi = ContractConfig.Abi;
            var contract = web3.Eth.GetContract(abi, contractAddress);
            var getTokenMetadataFunction = contract.GetFunction("getTokenMetadata");
            var result = await getTokenMetadataFunction.CallAsync<TokenMetadata>(tokenId);
            return result.BranchPath;
        }

        private void ImportLatestModel(RhinoDoc doc, string branchPath)
        {
            if (!System.IO.File.Exists(branchPath))
            {
                RhinoApp.WriteLine($"File not found: {branchPath}");
                return;
            }

            var branchHistory = JsonConvert.DeserializeObject<List<ModelState>>(System.IO.File.ReadAllText(branchPath));
            if (branchHistory == null || !branchHistory.Any())
            {
                RhinoApp.WriteLine("No valid history found in the input file");
                return;
            }

            var latestState = branchHistory.OrderByDescending(s => s.Timestamp).First();
            int addedCount = 0;
            int updatedCount = 0;
            int deletedCount = 0;

            foreach (var change in latestState.Changes)
            {
                switch (change.ChangeType)
                {
                    case "Added":
                        var addedGuid = ImportObject(doc, change);
                        if (addedGuid != Guid.Empty)
                            importedObjectIds.Add(addedGuid);
                        addedCount++;
                        break;
                    case "Modified":
                        var modifiedGuid = UpdateObject(doc, change);
                        if (modifiedGuid != Guid.Empty)
                            importedObjectIds.Add(modifiedGuid);
                        updatedCount++;
                        break;
                    case "Deleted":
                        if (DeleteObject(doc, change))
                            deletedCount++;
                        break;
                }
            }

            RhinoApp.WriteLine($"Imported latest model from branch: {latestState.BranchName}, Commit: {latestState.CommitId}");
            RhinoApp.WriteLine($"Added: {addedCount}, Updated: {updatedCount}, Deleted: {deletedCount} objects");
        }

        private void GenerateAndLayoutMake2D(RhinoDoc doc, double pitch)
        {
            RhinoApp.WriteLine($"Starting GenerateAndLayoutMake2D. Imported objects: {importedObjectIds.Count}");

            List<BoundingBox> boundingBoxes = new List<BoundingBox>();

            // 0度回転の描画
            boundingBoxes.Add(GetBoundingBox(doc, importedObjectIds));
            List<GeometryBase> make2D_0deg = GenerateMake2D(doc, importedObjectIds);
            RhinoApp.WriteLine($"Generated Make2D for 0 degrees. Results: {make2D_0deg.Count}");

            // 90度回転のオブジェクトを複製して回転
            RotateImportedObjects(doc, 0, Vector3d.XAxis);
            boundingBoxes.Add(GetBoundingBox(doc, importedObjectIds));
            List<GeometryBase> make2D_90deg = GenerateMake2D(doc, importedObjectIds);
            RhinoApp.WriteLine($"Generated Make2D for 90 degrees. Results: {make2D_90deg.Count}");

            // 45度回転のオブジェクトを複製して回転
            RotateImportedObjects(doc, 0, Vector3d.XAxis);
            rotatedObjectIds = DuplicateAndRotateObjects(doc, importedObjectIds, -90, Vector3d.XAxis);
            boundingBoxes.Add(GetBoundingBox(doc, rotatedObjectIds));
            List<GeometryBase> make2D_45deg = GenerateMake2D(doc, rotatedObjectIds);
            RhinoApp.WriteLine($"Generated Make2D for 45 degrees. Results: {make2D_45deg.Count}");

            // Y軸90度回転のオブジェクトを複製して回転
            RotateImportedObjects(doc, -90, Vector3d.XAxis);
            rotatedObjectIds = DuplicateAndRotateObjects(doc, importedObjectIds, -90, Vector3d.YAxis);
            boundingBoxes.Add(GetBoundingBox(doc, rotatedObjectIds));
            List<GeometryBase> make2D_90Ydeg = GenerateMake2D(doc, rotatedObjectIds);
            RhinoApp.WriteLine($"Generated Make2D for Y-axis 90 degrees. Results: {make2D_90Ydeg.Count}");

            // Make2D結果の配置と追加
            int added_0deg = AddMake2DToDocument(doc, make2D_0deg, "Make2D_0deg", Point3d.Origin);
            RhinoApp.WriteLine($"Added {added_0deg} objects for 0 degree Make2D");

            Vector3d translation = CalculateTranslation(1, boundingBoxes, pitch);
            int added_90deg = AddMake2DToDocument(doc, make2D_90deg, "Make2D_90deg", new Point3d(translation.X, 0, 0));
            RhinoApp.WriteLine($"Added {added_90deg} objects for 90 degree Make2D");

            translation = CalculateTranslation(2, boundingBoxes, pitch);
            int added_45deg = AddMake2DToDocument(doc, make2D_45deg, "Make2D_45deg", new Point3d(translation.X, 0, 0));
            RhinoApp.WriteLine($"Added {added_45deg} objects for 45 degree Make2D");

            translation = CalculateTranslation(3, boundingBoxes, pitch);
            int added_90Ydeg = AddMake2DToDocument(doc, make2D_90Ydeg, "Make2D_90Ydeg", new Point3d(translation.X, 0, 0));
            RhinoApp.WriteLine($"Added {added_90Ydeg} objects for Y-axis 90 degree Make2D");

            doc.Views.Redraw();
        }

        private Guid ImportObject(RhinoDoc doc, ObjectChange change)
        {
            var obj = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
            if (obj != null)
            {
                var attributes = new ObjectAttributes();
                attributes.ObjectId = change.Id;
                return doc.Objects.Add(obj, attributes);
            }
            return Guid.Empty;
        }

        private Guid UpdateObject(RhinoDoc doc, ObjectChange change)
        {
            var existingObj = doc.Objects.FindId(change.Id);
            if (existingObj != null)
            {
                var newGeometry = ModelDiffCommand.Instance.DeserializeObject(change.SerializedGeometry);
                if (newGeometry != null)
                {
                    var newAttributes = existingObj.Attributes.Duplicate();
                    if (doc.Objects.Replace(existingObj.Id, newGeometry, true))
                    {
                        return existingObj.Id;
                    }
                }
            }
            else
            {
                return ImportObject(doc, change);
            }
            return Guid.Empty;
        }

        private bool DeleteObject(RhinoDoc doc, ObjectChange change)
        {
            var existingObj = doc.Objects.Find(change.Id);
            if (existingObj != null)
            {
                doc.Objects.Delete(existingObj, true);
                return true;
            }
            return false;
        }

        private void RotateImportedObjects(RhinoDoc doc, double angle, Vector3d axis)
        {
            BoundingBox bbox = BoundingBox.Empty;
            foreach (var objId in importedObjectIds)
            {
                var obj = doc.Objects.FindId(objId);
                if (obj != null)
                {
                    bbox.Union(obj.Geometry.GetBoundingBox(true));
                }
            }
            Point3d center = bbox.Center;

            Transform rotation = Transform.Rotation(angle * Math.PI / 180.0, axis, center);

            foreach (var objId in importedObjectIds)
            {
                var obj = doc.Objects.FindId(objId);
                if (obj != null)
                {
                    doc.Objects.Transform(obj, rotation, true);
                }
            }
        }

        private List<GeometryBase> GenerateMake2D(RhinoDoc doc, List<Guid> objectIds)
        {
            var view = doc.Views.ActiveView;
            if (view == null)
            {
                RhinoApp.WriteLine("Active view is null");
                return new List<GeometryBase>();
            }

            var hld_params = new HiddenLineDrawingParameters
            {
                AbsoluteTolerance = doc.ModelAbsoluteTolerance,
                IncludeTangentEdges = false,
                IncludeHiddenCurves = false
            };
            hld_params.SetViewport(view.ActiveViewport);

            foreach (var objId in objectIds)
            {
                var obj = doc.Objects.FindId(objId);
                if (obj != null)
                    hld_params.AddGeometry(obj.Geometry, Transform.Identity, obj.Id);
            }

            var hld = HiddenLineDrawing.Compute(hld_params, true);
            if (hld == null)
            {
                RhinoApp.WriteLine("HiddenLineDrawing.Compute returned null");
                return new List<GeometryBase>();
            }

            var flatten = Transform.PlanarProjection(Rhino.Geometry.Plane.WorldXY);
            BoundingBox page_box = hld.BoundingBox(true);
            var delta_2d = new Vector2d(0, 0) - new Vector2d(page_box.Min.X, page_box.Min.Y);
            var delta_3d = Transform.Translation(new Vector3d(delta_2d.X, delta_2d.Y, 0.0));
            flatten = delta_3d * flatten;

            List<GeometryBase> make2DGeometry = new List<GeometryBase>();

            foreach (var hld_curve in hld.Segments)
            {
                if (hld_curve?.ParentCurve == null || hld_curve.ParentCurve.SilhouetteType == SilhouetteType.None)
                    continue;

                var crv = hld_curve.CurveGeometry.DuplicateCurve();
                if (crv != null)
                {
                    crv.Transform(flatten);
                    if (hld_curve.SegmentVisibility == HiddenLineDrawingSegment.Visibility.Visible)
                    {
                        make2DGeometry.Add(crv);
                    }
                }
            }

            foreach (var hld_pt in hld.Points)
            {
                if (hld_pt == null)
                    continue;

                var pt = hld_pt.Location;
                if (pt.IsValid)
                {
                    pt.Transform(flatten);
                    if (hld_pt.PointVisibility == HiddenLineDrawingPoint.Visibility.Visible)
                    {
                        make2DGeometry.Add(new Point(pt));
                    }
                }
            }
            doc.Views.Redraw();

            return make2DGeometry;
        }

        private List<Guid> DuplicateAndRotateObjects(RhinoDoc doc, List<Guid> objectIds, double angle, Vector3d axis)
        {
            List<Guid> newIds = new List<Guid>();
            Transform rotation = Transform.Rotation(angle * Math.PI / 180.0, axis, Point3d.Origin);

            foreach (var objId in objectIds)
            {
                var obj = doc.Objects.FindId(objId);
                if (obj != null)
                {
                    var duplicateGeometry = obj.Geometry.Duplicate();
                    duplicateGeometry.Transform(rotation);
                    var newId = doc.Objects.Add(duplicateGeometry);
                    if (newId != Guid.Empty)
                    {
                        newIds.Add(newId);
                    }
                }
            }

            return newIds;
        }

        private int AddMake2DToDocument(RhinoDoc doc, List<GeometryBase> geometries, string layerName, Point3d basePoint)
        {
            var layer = CreateOrGetLayer(doc, layerName);
            var attributes = new ObjectAttributes { LayerIndex = layer.Index };
            Transform move = Transform.Translation(basePoint - Point3d.Origin);
            int addedCount = 0;

            foreach (var geo in geometries)
            {
                var dupGeo = geo.Duplicate();
                dupGeo.Transform(move);
                if (dupGeo is Curve curve)
                {
                    if (doc.Objects.AddCurve(curve, attributes) != Guid.Empty)
                        addedCount++;
                }
                else if (dupGeo is Point point)
                {
                    if (doc.Objects.AddPoint(point.Location, attributes) != Guid.Empty)
                        addedCount++;
                }
            }

            return addedCount;
        }

        private Layer CreateOrGetLayer(RhinoDoc doc, string layerName)
        {
            var layer = doc.Layers.FindName(layerName);
            if (layer == null)
            {
                layer = new Layer { Name = layerName };
                doc.Layers.Add(layer);
            }
            return layer;
        }

        private BoundingBox GetBoundingBox(RhinoDoc doc, List<Guid> objectIds)
        {
            BoundingBox bbox = BoundingBox.Empty;
            foreach (var objId in objectIds)
            {
                var obj = doc.Objects.FindId(objId);
                if (obj != null)
                {
                    bbox.Union(obj.Geometry.GetBoundingBox(true));
                }
            }
            return bbox;
        }

        private Vector3d CalculateTranslation(int index, List<BoundingBox> boundingBoxes, double pitch)
        {
            double x = 0;
            for (int i = 0; i < index; i++)
            {
                x += boundingBoxes[i].Diagonal.X + pitch;
            }
            return new Vector3d(x, 0, 0);
        }

        private void RemoveAllModelObjects(RhinoDoc doc)
        {
            List<Guid> allObjectIds = new List<Guid>();
            allObjectIds.AddRange(importedObjectIds);
            allObjectIds.AddRange(rotatedObjectIds);

            foreach (var id in allObjectIds)
            {
                if (doc.Objects.FindId(id) != null)
                {
                    doc.Objects.Delete(id, true);
                }
            }

            importedObjectIds.Clear();
            rotatedObjectIds.Clear();

            doc.Views.Redraw();
            RhinoApp.WriteLine("All model objects have been removed.");
        }

        [FunctionOutput]
        private class TokenMetadata
        {
            [Parameter("string", "name", 1)]
            public string Name { get; set; }

            [Parameter("string", "maker", 2)]
            public string Maker { get; set; }

            [Parameter("address", "maker_address", 3)]
            public string MakerAddress { get; set; }

            [Parameter("string", "date", 4)]
            public string Date { get; set; }

            [Parameter("string", "imagePath", 5)]
            public string ImagePath { get; set; }

            [Parameter("string", "branchPath", 6)]
            public string BranchPath { get; set; }

            [Parameter("uint256[]", "parentIds", 7)]
            public List<BigInteger> ParentIds { get; set; }
        }
    }
}
