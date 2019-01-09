using Guytp.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Sift.DividendPayer
{
    public class BlockchainScanner
    {
        private string _rpcUrl;

        protected readonly Web3 _web3;

        public BlockchainScanner()
        {
            _rpcUrl = AppConfig.ApplicationInstance.GetAppSetting<string>("RpcUrl");
            _web3 = new Web3(_rpcUrl);
        }

        public List<SnapshotItem> Scan(DateTime scanDate, string outputFile)
        {
            Console.WriteLine("Scanning blockchain against " + _rpcUrl);

            // First we determine which block we go to
            ulong blockForTime = GetBlockBeforeTime(scanDate);
            Console.WriteLine("Last eligible block was " + blockForTime);

            // Now we want to determine holders of old SIFT and new SIFT
            List<SnapshotItem> siftHolders = GetHoldersForContract("0x8a187d5285d316bcbc9adafc08b51d70a0d8e000", 4102075, blockForTime, 0);
            List<SnapshotItem> xsftHolders = GetHoldersForContract("0x1d074266bca9481bdeee504836cfefee69092a28", 5242598, blockForTime, 6);
            SnapshotItem hotWallet = xsftHolders.FirstOrDefault(si => si.Address == "0x43b0eb4dfe7a3a86b4805b6db07e80c285b54553");
            if (hotWallet != null)
                hotWallet.Balance -= 1122; // Fixes issue where we over-issued some SIFT

            // Consolidate the two lists into a single list of SIFT holders
            List<SnapshotItem> finalList = new List<SnapshotItem>(siftHolders.Where(h => h.Address != "0x0000000000000000000000000000000000000000").ToList());
            foreach (SnapshotItem item in xsftHolders)
            {
                if (item.Address == "0x0000000000000000000000000000000000000000")
                    continue;
                SnapshotItem existing = finalList.FirstOrDefault(h => h.Address == item.Address);
                if (existing != null)
                    existing.Balance += item.Balance;
                else
                    finalList.Add(item);
            }

            // Return our consolidated list
            return finalList.OrderByDescending(item => item.Balance).ToList();
        }

        private List<SnapshotItem> GetHoldersForContract(string contract, ulong fromHeight, ulong lastBlock, int decimals)
        {
            Console.WriteLine("Checking " + contract + " from " + fromHeight + " to " + lastBlock);
            const string contractAbi = "[{\"constant\":true,\"inputs\":[],\"name\":\"name\",\"outputs\":[{\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"name\":\"_spender\",\"type\":\"address\"},{\"name\":\"_amount\",\"type\":\"uint256\"}],\"name\":\"approve\",\"outputs\":[{\"name\":\"success\",\"type\":\"bool\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"totalSupply\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"name\":\"_from\",\"type\":\"address\"},{\"name\":\"_to\",\"type\":\"address\"},{\"name\":\"_amount\",\"type\":\"uint256\"}],\"name\":\"transferFrom\",\"outputs\":[{\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"decimals\",\"outputs\":[{\"name\":\"\",\"type\":\"uint8\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[{\"name\":\"_owner\",\"type\":\"address\"}],\"name\":\"balanceOf\",\"outputs\":[{\"name\":\"balance\",\"type\":\"uint256\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[{\"name\":\"_index\",\"type\":\"uint256\"}],\"name\":\"tokenHolder\",\"outputs\":[{\"name\":\"\",\"type\":\"address\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"symbol\",\"outputs\":[{\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"icoContractAddress\",\"outputs\":[{\"name\":\"\",\"type\":\"address\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"contractVersion\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"name\":\"_to\",\"type\":\"address\"},{\"name\":\"_amount\",\"type\":\"uint256\"}],\"name\":\"transfer\",\"outputs\":[{\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"isClosed\",\"outputs\":[{\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"tokenHolderCount\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":true,\"inputs\":[{\"name\":\"_owner\",\"type\":\"address\"},{\"name\":\"_spender\",\"type\":\"address\"}],\"name\":\"allowance\",\"outputs\":[{\"name\":\"remaining\",\"type\":\"uint256\"}],\"payable\":false,\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"name\":\"_address\",\"type\":\"address\"},{\"name\":\"_amount\",\"type\":\"uint256\"}],\"name\":\"mintTokens\",\"outputs\":[],\"payable\":false,\"type\":\"function\"},{\"inputs\":[{\"name\":\"_icoContractAddress\",\"type\":\"address\"},{\"name\":\"_authenticationManagerAddress\",\"type\":\"address\"}],\"payable\":false,\"type\":\"constructor\"},{\"anonymous\":false,\"inputs\":[],\"name\":\"FundClosed\",\"type\":\"event\"},{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"name\":\"from\",\"type\":\"address\"},{\"indexed\":true,\"name\":\"to\",\"type\":\"address\"},{\"indexed\":false,\"name\":\"value\",\"type\":\"uint256\"}],\"name\":\"Transfer\",\"type\":\"event\"},{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"name\":\"_owner\",\"type\":\"address\"},{\"indexed\":true,\"name\":\"_spender\",\"type\":\"address\"},{\"indexed\":false,\"name\":\"_value\",\"type\":\"uint256\"}],\"name\":\"Approval\",\"type\":\"event\"}]";
            Contract ethContract = _web3.Eth.GetContract(contractAbi, contract);
            Event transferEvent = ethContract.GetEvent("Transfer");

            ulong startBlock = fromHeight;
            Dictionary<string, decimal> balances = new Dictionary<string, decimal>();
            while (startBlock < lastBlock)
            {
                List<EventLog<Erc20TransferEvent>> transferEventLogs = null;
                int tryCount = 0;
                int increment = 65000;
                ulong endBlock = startBlock;
                while (true)
                    try
                    {
                        tryCount++;
                        endBlock = startBlock + (ulong)increment;
                        if (endBlock > lastBlock)
                            endBlock = lastBlock;
                        HexBigInteger filterId = transferEvent.CreateFilterAsync(null, fromBlock: new BlockParameter(startBlock), toBlock: new BlockParameter(endBlock)).Result;
                        Console.WriteLine("Scanning from " + startBlock + " to " + endBlock);
                        transferEventLogs = transferEvent.GetAllChanges<Erc20TransferEvent>(filterId).Result;
                        break;
                    }
                    catch (Exception ex)
                    {
                        increment = (int)((double)increment * 0.5);
                        if (increment < 1)
                            increment = 1;
                        if (ex.Message.Contains("Rpc timeout afer") && tryCount < 10)
                        {
                            Console.WriteLine("Node timeout, increment now " + increment);
                            continue;
                        }
                        throw;
                    }
                foreach (EventLog<Erc20TransferEvent> transferEventLog in transferEventLogs)
                {
                    Erc20TransferEvent e = transferEventLog.Event;
                    decimal value = (decimal)(e.Value);
                    if (decimals > 0)
                        value /= (decimal)Math.Pow(10, decimals);
                    if (!balances.ContainsKey(e.From))
                        balances.Add(e.From, 0);
                    if (!balances.ContainsKey(e.To))
                        balances.Add(e.To, 0);
                    balances[e.From] -= value;
                    balances[e.To] += value;
                    Console.WriteLine("Transfer from " + e.From + " (" + balances[e.From] + ") to " + e.To + "(" + balances[e.To] +")");
                }
                startBlock = endBlock + 1;
            }
            return balances.Where(kvp => kvp.Value != 0).Select(kvp => new SnapshotItem(kvp.Key, kvp.Value)).ToList();
        }

        private ulong GetBlockBeforeTime(DateTime date)
        {
            ulong currentBlock = ulong.Parse(_web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().Result.Value.ToString());
            ulong checkBlock = currentBlock - (ulong)(DateTime.UtcNow.Subtract(date).TotalSeconds / 14);
            Console.WriteLine("Current block is " + currentBlock + " checking " + checkBlock);

            // Keep moving bakc until we're behind the target date
            BlockWithTransactionHashes block = _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger((BigInteger)checkBlock)).Result;
            if (block == null)
                throw new Exception("Unable to load block " + checkBlock);
            DateTime blockDate = GetDate((ulong)block.Timestamp.Value);
            int count = 0;
            ulong lastAdjustment = 0;
            int bounceCount = 0;
            bool wasLastAdjustmentForward = false;
            while (true)
            {
                count++;
                bool doLog = true;//count % 10 == 0;
                if (blockDate > date)
                {
                    TimeSpan differenceFromTarget = blockDate.Subtract(date);
                    if (doLog)
                        Console.WriteLine("Start point of " + checkBlock + " was too far in front, moving back");
                    ulong adjustment = (ulong)(differenceFromTarget.TotalSeconds / 9);
                    if (adjustment == lastAdjustment)
                        adjustment += 2;
                    lastAdjustment = adjustment;
                    checkBlock -= adjustment;
                    if (wasLastAdjustmentForward)
                        bounceCount++;
                    else
                        bounceCount = 0;
                    wasLastAdjustmentForward = false;
                }
                else if (blockDate < date)
                {
                    TimeSpan differenceFromTarget = date.Subtract(blockDate);
                    // If within a 10 minute window break, otherwise adjust start point
                    if (differenceFromTarget.TotalSeconds < 600 || bounceCount > 5)
                        break;
                    if (doLog)
                        Console.WriteLine("Start point of " + checkBlock + " was too far behind, moving forward");
                    ulong adjustment = (ulong)(differenceFromTarget.TotalSeconds / 7);
                    if (adjustment == lastAdjustment)
                        adjustment -= 3;
                    if (!wasLastAdjustmentForward)
                        bounceCount++;
                    else
                        bounceCount = 0;
                    wasLastAdjustmentForward = true;
                    lastAdjustment = adjustment;
                    checkBlock += adjustment;
                }
                block = _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger((BigInteger)checkBlock)).Result;
                blockDate = GetDate((ulong)block.Timestamp.Value);
            }

            // We know we're behind the time we're looking for so let's move forward one block at a time until we find it
            count = 0;
            while (checkBlock <= currentBlock)
            {
                count++;
                bool doLog = count % 10 == 0;
                block = _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger((BigInteger)checkBlock)).Result;
                DateTime blockTime = GetDate((ulong)block.Timestamp.Value);
                if (blockTime > date)
                    return checkBlock - 1;
                if (doLog)
                    Console.WriteLine("Block " + checkBlock + " has time of " + blockTime + ", moving to next");
                checkBlock++;
            }
            throw new Exception("Could not find a block immediately before " + date.ToString());
        }

        private static DateTime GetDate(ulong timestamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp);
        }
    }
}