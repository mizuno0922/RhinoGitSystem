using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ipfs.Http;
using Nethereum.Web3;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Rhino;
using Rhino.Commands;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Config;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.Commands.Sync.Pull
{
    public class PullBranchCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;

        public PullBranchCommand()
        {
            Instance = this;
            string localIpAddress = "192.168.0.144";
            ipfs = new IpfsClient($"http://{localIpAddress}:5001");
            ganacheUrl = $"http://localhost:7545";
        }

        public static PullBranchCommand Instance { get; private set; }

        public override string EnglishName => "PullBranch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string tokenIdInput = GetStringInput("Enter NFT Token ID");
            if (string.IsNullOrEmpty(tokenIdInput) || !BigInteger.TryParse(tokenIdInput, out BigInteger tokenId))
            {
                RhinoApp.WriteLine("Invalid Token ID");
                return Result.Failure;
            }

            Task.Run(async () =>
            {
                try
                {
                    var branchContent = await GetBranchPathFromNFT(tokenId);
                    if (string.IsNullOrEmpty(branchContent))
                    {
                        RhinoApp.WriteLine($"Branch path not found for Token ID: {tokenId}");
                        return;
                    }

                    PullBranch(doc, branchContent);
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
            var gs = new Rhino.Input.Custom.GetString();
            gs.SetCommandPrompt(prompt);
            if (!string.IsNullOrEmpty(defaultValue))
                gs.SetDefaultString(defaultValue);

            if (gs.Get() != Rhino.Input.GetResult.String)
                return defaultValue;

            return gs.StringResult();
        }

        private async Task<string> GetBranchPathFromNFT(BigInteger tokenId)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            var contract = web3.Eth.GetContract(ContractConfig.Abi, contractAddress);
            var getTokenMetadataFunction = contract.GetFunction("getTokenMetadata");

            var result = await getTokenMetadataFunction.CallAsync<TokenMetadata>(tokenId);
            return result.BranchPath;
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

            // Update model history and branch information
            UpdateModelHistory(pullBranchHistory);
            UpdateBranchInfo(branchName, pullBranchHistory);

            // Reconstruct model to the latest state
            var latestMainCommit = pullBranchHistory.LastOrDefault()?.CommitId;
            if (!string.IsNullOrEmpty(latestMainCommit))
            {
                ModelDiffCommand.Instance.ReconstructModel(doc, latestMainCommit);
            }

            RhinoApp.WriteLine($"Branch '{branchName}' history pulled and model updated");
        }

        private void UpdateModelHistory(List<ModelState> pullBranchHistory)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var updatedHistory = new List<ModelState>(modelHistory);
            var processedCommits = new HashSet<string>();

            foreach (var state in pullBranchHistory)
            {
                if (processedCommits.Contains(state.CommitId))
                    continue;

                var existingState = updatedHistory.FirstOrDefault(s => s.CommitId == state.CommitId);
                if (existingState == null)
                {
                    AddNewCommitWithParentCheck(updatedHistory, state);
                    RhinoApp.WriteLine($"Added new commit: {state.CommitId}");
                }
                else if (!ModelStatesAreEqual(existingState, state))
                {
                    UpdateExistingCommit(existingState, state);
                    RhinoApp.WriteLine($"Updated existing commit: {state.CommitId}");
                }

                processedCommits.Add(state.CommitId);
            }

            updatedHistory = updatedHistory.OrderBy(s => s.Timestamp).ToList();
            RebuildParentChildRelationships(updatedHistory);
            ModelDiffCommand.Instance.SaveModelHistory(updatedHistory);
        }

        private void AddNewCommitWithParentCheck(List<ModelState> history, ModelState newState)
        {
            var parentCommit = history.FirstOrDefault(s => s.CommitId == newState.ParentCommit);
            if (parentCommit == null)
            {
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

        private void UpdateBranchInfo(string branchName, List<ModelState> branchHistory)
        {
            var branches = ModelDiffCommand.Instance.GetBranches(ModelDiffCommand.Instance.FileId);
            var branch = branches.FirstOrDefault(b => b.Name == branchName);
            if (branch == null)
            {
                branch = new RhinoGitSystem.Models.Branch { Name = branchName, Commits = new List<string>() };
                branches.Add(branch);
                RhinoApp.WriteLine($"Created new branch: {branchName}");
            }

            var newCommits = branchHistory.Select(s => s.CommitId).Except(branch.Commits).ToList();
            branch.Commits.AddRange(newCommits);
            branch.Commits = branch.Commits.Distinct()
                .OrderBy(c => branchHistory.FindIndex(s => s.CommitId == c))
                .ToList();

            ModelDiffCommand.Instance.SaveBranches(branches);
            RhinoApp.WriteLine($"Updated branch '{branchName}' with {newCommits.Count()} new commits");
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
