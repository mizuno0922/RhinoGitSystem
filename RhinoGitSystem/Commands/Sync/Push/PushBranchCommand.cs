using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ipfs.Http;
using Nethereum.Web3;
using Nethereum.Contracts;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using System.Globalization;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using Nethereum.Hex.HexTypes;
using Rhino.Input.Custom;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Config;

namespace RhinoGitSystem.Commands.Sync.Push
{
    public class PushBranchCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;

        public PushBranchCommand()
        {
            Instance = this;
            string localIpAddress = "192.168.0.144";
            ipfs = new IpfsClient($"http://{localIpAddress}:5001");
            ganacheUrl = $"http://localhost:7545";
        }

        public static PushBranchCommand Instance { get; private set; }

        public override string EnglishName => "PushBranch";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string branchName = string.Empty;
            var result = RhinoGet.GetString("Enter branch name to push", false, ref branchName);
            if (result != Result.Success || string.IsNullOrEmpty(branchName))
                return Result.Cancel;

            Task.Run(async () =>
            {
                string jsonContent = await PushBranch(doc, branchName);
                MintNFT(doc, branchName, jsonContent);
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

        private async Task<string> PushBranch(RhinoDoc doc, string branchName)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var branchHistory = modelHistory.Where(s => s.BranchName == branchName).ToList();

            if (!branchHistory.Any())
            {
                RhinoApp.WriteLine($"No history found for branch '{branchName}'");
                return string.Empty;
            }

            BigInteger latestNftId = await GetLatestNftId();

            string baseBranchName;
            int previousNumber;
            ParseBranchName(branchName, out baseBranchName, out previousNumber);

            string newNftName = $"{baseBranchName}_{latestNftId + 1}";
            string jsonContent = JsonConvert.SerializeObject(branchHistory, Formatting.Indented);

            foreach (var state in branchHistory)
            {
                state.BranchName = newNftName;
                if (state.Message.Contains("ブランチ '"))
                {
                    state.Message = state.Message.Replace($"ブランチ '{state.BranchName}'", $"ブランチ '{newNftName}'");
                }
            }

            string updatedJsonContent = JsonConvert.SerializeObject(branchHistory, Formatting.Indented);
            string outputPath = GetOutputPath(doc, newNftName);
            File.WriteAllText(outputPath, updatedJsonContent);

            RhinoApp.WriteLine($"Previous branch number: {branchName}, New branch number: {newNftName}");
            return outputPath;
        }

        private string GetOutputPath(RhinoDoc doc, string newNftName)
        {
            string baseDirectory = @"\\Mac\Home\Downloads\architecture\ADL\卒プロ\GitTest\data\branch";
            string fileName = $"{newNftName}.json";
            return Path.Combine(baseDirectory, fileName);
        }

        private void ParseBranchName(string branchName, out string baseBranchName, out int previousNumber)
        {
            int underscoreIndex = branchName.LastIndexOf('_');
            if (underscoreIndex != -1 && int.TryParse(branchName.Substring(underscoreIndex + 1), out previousNumber))
            {
                baseBranchName = branchName.Substring(0, underscoreIndex);
            }
            else
            {
                baseBranchName = branchName;
                previousNumber = 0;
            }
        }

        private async Task<BigInteger> GetLatestNftId()
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            string abi = ContractConfig.Abi;

            var contract = web3.Eth.GetContract(abi, contractAddress);
            var getLatestTokenIdFunction = contract.GetFunction("getLatestTokenId");
            var result = await getLatestTokenIdFunction.CallAsync<LatestTokenIdOutput>();
            return result.TokenId;
        }

        public async Task<BigInteger?> GetNftIdFromBranchName(string branchName)
        {
            var latestTokenId = await GetLatestNftId();

            for (BigInteger i = 1; i <= latestTokenId; i++)
            {
                var metadata = await GetTokenMetadata(i);
                if (metadata.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return null;
        }

        private async Task<TokenMetadata> GetTokenMetadata(BigInteger tokenId)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            string abi = ContractConfig.Abi;
            var contract = web3.Eth.GetContract(abi, contractAddress);
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

        public async Task<BigInteger?> GetMaxNftIdFrommain()
        {
            var latestTokenId = await GetLatestNftId();
            BigInteger? maxMainId = null;

            for (BigInteger i = 0; i <= latestTokenId; i++)
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

        private async void MintNFT(RhinoDoc doc, string branchName, string branchContent)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            string abi = ContractConfig.Abi;
            var contract = web3.Eth.GetContract(abi, contractAddress);
            var mintFunction = contract.GetFunction("mintNFT");

            BigInteger latestNftId = await GetLatestNftId();

            // Parse the branch name to get previousNumber
            string baseBranchName;
            int previousNumber;
            ParseBranchName(branchName, out baseBranchName, out previousNumber);

            string newNftName = $"{baseBranchName}_{latestNftId + 1}";

            string name = newNftName;
            string maker = GetStringInput("Enter your name");
            string imagePath = "";
            string fromAddress = GetStringInput("Enter your Ethereum address");
            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string parentIdsInput = "";

            if (previousNumber == 0)
            {
                BigInteger? mainId = await GetMaxNftIdFrommain();
                parentIdsInput = mainId.HasValue ? mainId.Value.ToString() : "";
            }
            else
            {
                parentIdsInput = previousNumber.ToString();
            }

            List<BigInteger> parentIds = new List<BigInteger>();
            foreach (string id in parentIdsInput.Split(','))
            {
                if (BigInteger.TryParse(id.Trim(), out BigInteger parsedId))
                {
                    parentIds.Add(parsedId);
                }
            }

            try
            {
                var gas = await mintFunction.EstimateGasAsync(fromAddress, null, null, parentIds, name, maker, date, imagePath, branchContent);
                gas = new HexBigInteger(gas.Value * 2);
                var value = new HexBigInteger(0);
                var receipt = await mintFunction.SendTransactionAndWaitForReceiptAsync(fromAddress, gas, value, null,
                    parentIds, name, maker, date, imagePath, branchContent);

                RhinoApp.WriteLine($"NFT minted successfully! Transaction hash: {receipt.TransactionHash}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error minting NFT: {ex.Message}");
            }
        }

        [FunctionOutput]
        private class LatestTokenIdOutput
        {
            [Parameter("uint256", "", 1)]
            public BigInteger TokenId { get; set; }
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
    }
}
