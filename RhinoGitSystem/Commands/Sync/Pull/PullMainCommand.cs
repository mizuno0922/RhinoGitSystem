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
    public class PullMainCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;

        public PullMainCommand()
        {
            Instance = this;
            string localIpAddress = "192.168.0.144";
            ipfs = new IpfsClient($"http://{localIpAddress}:5001");
            ganacheUrl = $"http://localhost:7545";
        }

        public static PullMainCommand Instance { get; private set; }

        public override string EnglishName => "PullMain";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Task.Run(async () =>
            {
                try
                {
                    BigInteger? mainId = await GetMaxNftIdFrommain();
                    string mainmaxId = mainId.HasValue ? mainId.Value.ToString() : "";
                    BigInteger.TryParse(mainmaxId, out BigInteger tokenId);
                    var branchPath = await GetBranchPathFromNFT(tokenId);
                    if (string.IsNullOrEmpty(branchPath))
                    {
                        RhinoApp.WriteLine($"Branch path not found for Token ID: {mainmaxId}");
                        return;
                    }

                    PullMain(doc, branchPath);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Error: {ex.Message}");
                }
            }).Wait();

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

        private void UpdateModelHistory(List<ModelState> pullMainHistory)
        {
            var fullHistory = ModelDiffCommand.Instance.GetModelHistory();

            // Remove all existing main branch commits
            fullHistory.RemoveAll(s => s.BranchName == "main");

            // Add pulled main branch history
            fullHistory.AddRange(pullMainHistory);

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

        private async Task<string> GetBranchPathFromNFT(BigInteger tokenId)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            var contract = web3.Eth.GetContract(ContractConfig.Abi, contractAddress);
            var getTokenMetadataFunction = contract.GetFunction("getTokenMetadata");
            var result = await getTokenMetadataFunction.CallAsync<TokenMetadata>(tokenId);
            return result.BranchPath;
        }

        private async Task<TokenMetadata> GetTokenMetadata(BigInteger tokenId)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            var contract = web3.Eth.GetContract(ContractConfig.Abi, contractAddress);
            var getTokenMetadataFunction = contract.GetFunction("getTokenMetadata");
            var result = await getTokenMetadataFunction.CallDeserializingToObjectAsync<TokenMetadataDTO>(tokenId);
            return new TokenMetadata
            {
                Name = result.Name,
                Maker = result.Maker,
                MakerAddress = result.MakerAddress,
                Date = result.Date,
                ImagePath = result.ImagePath,
                BranchPath = result.BranchPath,
                ParentIds = result.ParentIds
            };
        }

        private async Task<BigInteger> GetLatestNftId()
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            var contract = web3.Eth.GetContract(ContractConfig.Abi, contractAddress);
            var getLatestTokenIdFunction = contract.GetFunction("getLatestTokenId");
            var result = await getLatestTokenIdFunction.CallAsync<LatestTokenIdOutput>();
            return result.TokenId;
        }

        public async Task<BigInteger?> GetMaxNftIdFrommain()
        {
            var latestTokenId = await GetLatestNftId();
            BigInteger? maxMainId = null;

            for (BigInteger i = 1; i <= latestTokenId; i++)
            {
                var metadata = await GetTokenMetadata(i);
                if (metadata.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                {
                    if (!maxMainId.HasValue || i > maxMainId.Value)
                    {
                        maxMainId = i;
                    }
                }
            }

            return maxMainId;
        }

        [FunctionOutput]
        private class TokenMetadataDTO
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

        private class TokenMetadata
        {
            public string Name { get; set; }
            public string Maker { get; set; }
            public string MakerAddress { get; set; }
            public string Date { get; set; }
            public string ImagePath { get; set; }
            public string BranchPath { get; set; }
            public List<BigInteger> ParentIds { get; set; }
        }

        [FunctionOutput]
        private class LatestTokenIdOutput
        {
            [Parameter("uint256", "", 1)]
            public BigInteger TokenId { get; set; }
        }
    }
}
