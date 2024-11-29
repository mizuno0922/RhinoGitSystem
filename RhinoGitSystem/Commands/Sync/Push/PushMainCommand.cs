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
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using System.Globalization;
using Nethereum.Hex.HexTypes;
using RhinoGitSystem.Commands.Model;
using RhinoGitSystem.Config;

namespace RhinoGitSystem.Commands.Sync.Push
{
    public class PushMainCommand : Command
    {
        private readonly IpfsClient ipfs;
        private string ganacheUrl;

        public PushMainCommand()
        {
            Instance = this;
            string localIpAddress = "192.168.0.144";
            ipfs = new IpfsClient($"http://{localIpAddress}:5001");
            ganacheUrl = $"http://localhost:7545";
        }

        public static PushMainCommand Instance { get; private set; }

        public override string EnglishName => "PushMain";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            string jsonContent = PushMain(doc).Result;
            MintNFT(doc, jsonContent);
            return Result.Success;
        }

        private async Task<string> PushMain(RhinoDoc doc)
        {
            var modelHistory = ModelDiffCommand.Instance.GetModelHistory();
            var mainHistory = modelHistory.Where(s => s.BranchName == "main").ToList();

            if (!mainHistory.Any())
            {
                RhinoApp.WriteLine("No history found for main branch");
                return string.Empty;
            }

            BigInteger latestNftId = await GetLatestNftId();
            string jsonContent = JsonConvert.SerializeObject(mainHistory, Formatting.Indented);
            string newNftName = $"main_{latestNftId + 1}";

            string outputPath = GetOutputPath(doc, newNftName);
            File.WriteAllText(outputPath, JsonConvert.SerializeObject(mainHistory, Formatting.Indented));
            RhinoApp.WriteLine($"Main branch history pushed to {newNftName}");

            return outputPath;
        }

        private async void MintNFT(RhinoDoc doc, string jsonContent)
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            string abi = ContractConfig.Abi;
            var contract = web3.Eth.GetContract(abi, contractAddress);
            var mintFunction = contract.GetFunction("mintNFT");

            BigInteger latestNftId = await GetLatestNftId();
            string newNftName = $"main_{latestNftId + 1}";
            string outputPath = GetOutputPath(doc, newNftName);

            string name = "main";
            string maker = GetStringInput("Enter your name");
            string imagePath = GetStringInput("Enter imagePath");
            string branchPath = jsonContent;
            string branchNFTID = GetStringInput("Enter branch NFTID to merge");
            string fromAddress = GetStringInput("Enter your Ethereum address");
            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            BigInteger tokenID = BigInteger.Parse(branchNFTID);
            var metadata = await GetTokenMetadata(tokenID);
            string branchName = metadata.Name;

            BigInteger? parentId = await GetNftIdFromBranchName(branchName);
            string branchId = parentId.HasValue ? parentId.Value.ToString() : "";

            BigInteger? mainId = await GetMaxNftIdFrommain();
            string mainmaxId = mainId.HasValue ? mainId.Value.ToString() : "";

            string parentIdsInput = $"{branchId},{mainmaxId}";

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
                var gas = new HexBigInteger(900000);
                var value = new HexBigInteger(0);
                var receipt = await mintFunction.SendTransactionAndWaitForReceiptAsync(fromAddress, gas, value, null,
                    parentIds, name, maker, date, imagePath, branchPath);

                RhinoApp.WriteLine($"NFT minted successfully! Transaction hash: {receipt.TransactionHash}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error minting NFT: {ex.Message}");
            }
        }

        private string GetOutputPath(RhinoDoc doc, string newNftName)
        {
            string baseDirectory = @"\\Mac\Home\Downloads\architecture\ADL\卒プロ\GitTest\data\branch";
            string fileName = $"{newNftName}.json";
            return Path.Combine(baseDirectory, fileName);
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

        private async Task<BigInteger> GetLatestNftId()
        {
            var web3 = new Web3(ganacheUrl);
            string contractAddress = ContractConfig.ContractAddress;
            var contract = web3.Eth.GetContract(ContractConfig.Abi, contractAddress);
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
