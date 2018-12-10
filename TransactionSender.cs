using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sift.DividendPayer
{
    public class TransactionSender
    {
        private string _rpcUrl;

        protected readonly Web3 _web3;
        public TransactionSender()
        {
            _rpcUrl = Guytp.Config.AppConfig.ApplicationInstance.GetAppSetting<string>("RpcUrl");
            _web3 = new Web3(_rpcUrl);
        }

        public void ProcessTransactions(string file, string privateKey, decimal amountToSend, ulong? startNonce)
        {
            // Load data from file
            List<SnapshotItem> recipients = LoadFromFile(file);

            // Determine sending address
            EthECKey ecKey = new EthECKey(privateKey);
            string sendingAddress = ecKey.GetPublicAddress();

            // Remove exclusions from recipients list
            ConfigExclusionAddress[] exclusionAddresses = Guytp.Config.AppConfig.ApplicationInstance.GetObject<ConfigExclusionAddress[]>("ExclusionAddresses");
            decimal excludedAmount = 0;
            if (exclusionAddresses != null)
                foreach (ConfigExclusionAddress exclusionAddress in exclusionAddresses)
                {
                    SnapshotItem item = recipients.FirstOrDefault(si => si.Address.ToLower() == exclusionAddress.Address.ToLower());
                    if (item != null)
                    {
                        recipients.Remove(item);
                        Console.WriteLine("Excluded " + item.Address + " (" + exclusionAddress.Notes + ")");
                        excludedAmount += item.Balance;
                    }
                }

            // Setup redirections from config
            ConfigRedirection[] redirections = Guytp.Config.AppConfig.ApplicationInstance.GetObject<ConfigRedirection[]>("Redirections");
            if (redirections != null)
                foreach (ConfigRedirection redirection in redirections)
                {
                    SnapshotItem from = recipients.FirstOrDefault(si => si.Address.ToLower() == redirection.From.ToLower());
                    if (from == null)
                        continue;
                    recipients.Remove(from);
                    SnapshotItem to = recipients.FirstOrDefault(si => si.Address.ToLower() == redirection.To.ToLower());
                    if (to == null)
                    {
                        to = new SnapshotItem(redirection.To, from.Balance);
                        recipients.Add(to);
                    }
                    else
                        to.Balance += from.Balance;
                    Console.WriteLine("Redirected " + from.Balance + " SIFT from " + from.Address + " to " + to.Address + "(" + redirection.Notes + ")");
                }

            // Determine gas price (avg + 10% + 1gwei)
            BigInteger gasPrice = 0;
            const ulong gasPerTransaction = 21000;
            Console.WriteLine("Checking gas price");
            while (true)
            {
                try
                {
                    WebRequest request = WebRequest.Create("https://ethgasstation.info/json/ethgasAPI.json");
                    request.Method = "GET";
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream dataStream = response.GetResponseStream())
                    using (StreamReader streamReader = new StreamReader(dataStream))
                    {
                        string json = streamReader.ReadToEnd();
                        if (response.StatusCode != HttpStatusCode.OK)
                            throw new Exception("EthGasStation gave us a " + response.StatusCode);
                        json = json.Replace("NaN", "0").Replace("{null}", "null"); // Weird deserialisation issues some times so let's fix them
                        JObject jobj = JObject.Parse(json);
                        decimal val = Math.Round(jobj["average"].ToObject<decimal>());
                        val *= 1.1m; // + 10%
                        val += 10m; // + 1gwei
                        gasPrice = (BigInteger)(val * 100000000);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to check gas price, waiting to try again: " + ex.Message);
                    Thread.Sleep(2500);
                }
            }

            // Ensure we have the balance to send
            decimal ethMultiplier = 1000000000000000000m;
            decimal balance = ((decimal)(BigInteger)_web3.Eth.GetBalance.SendRequestAsync(sendingAddress).Result.Value) / ethMultiplier;
            decimal gasCostPerTransaction = (decimal)gasPrice * gasPerTransaction / ethMultiplier;
            decimal amountRequired = amountToSend + (recipients.Count * gasCostPerTransaction);
            if (balance < amountRequired)
            {
                Console.WriteLine("Insufficient balance after factoring in gas");
                Console.WriteLine("    Balance:    " + balance);
                Console.WriteLine("    Required:   " + amountRequired);
                Console.WriteLine("    Difference: " + (amountRequired - balance));
                Console.Write("Continue [y/n]: ");
                while (true)
                {
                    char c = Console.ReadKey(true).KeyChar;
                    if (c == 'Y' || c == 'y')
                        break;
                    if (c == 'N' || c == 'n')
                    {
                        Console.WriteLine("Aborting");
                        return;
                    }
                }
                Console.WriteLine();
            }

            // If no nonce supplied, determine what last for account is now
            ulong nonce = 0;
            if (startNonce.HasValue)
                nonce = startNonce.Value;
            else
                nonce = (ulong)_web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(sendingAddress).Result.Value;

            // Determine amount per SIFT
            decimal validSift = recipients.Sum(si => si.Balance);
            decimal amountPerSift = amountToSend / validSift;
            if (validSift != 722935 - excludedAmount)
            {
                Console.WriteLine("WANING: Valid SIFT doesn't match expected");
                Console.WriteLine("    Got:        " + validSift);
                Console.WriteLine("    Expected:   " + (722935 - excludedAmount));
            }

            // Confirm the ETH per SIFT that's being paid out and ask for dummy, full or cancel
            Console.WriteLine("Dividend Payments");
            Console.WriteLine("    Excluded Amount:        " + excludedAmount);
            Console.WriteLine("    Recipients:             " + recipients.Count);
            Console.WriteLine("    Valid SIFT:             " + validSift);
            Console.WriteLine("    ETH per SIFT:           " + amountPerSift);
            Console.WriteLine("    From:                   " + sendingAddress);
            Console.WriteLine("    Nonce:                  " + nonce);
            Console.WriteLine("    Gas Price:              " + ((decimal)gasPrice / 10000000000m)  + " Gwei");
            bool isDummy = false;
            while (true)
            {
                Console.Write("Continue [dummy/real/cancel]: ");
                string option = Console.ReadLine().ToLower();
                if (option == "cancel")
                {
                    Console.WriteLine("Aborted send.");
                    return;
                }
                else if (option == "dummy")
                {
                    isDummy = true;
                    break;
                }
                else if (option == "real")
                    break;
            }

            // Start sending transactions one by one
            foreach (SnapshotItem recipient in recipients)
            {
                decimal dividendAmount = amountPerSift * recipient.Balance;
                Console.WriteLine(recipient.Address + " has " + recipient.Balance + " SIFT.  Dividend: " + dividendAmount);
                string txId = "[dummy]";
                try
                {
                    // Do the real send here
                    BigInteger sendAmount = UnitConversion.Convert.ToWei(dividendAmount);
                    Console.WriteLine("Send amount: " + sendAmount);
                    Console.WriteLine("Gas Price: " + gasPrice);
                    string transaction = new Nethereum.Signer.TransactionSigner().SignTransaction(privateKey, recipient.Address, sendAmount, nonce, gasPrice, 21000);
                    txId = new Nethereum.Util.Sha3Keccack().CalculateHashFromHex(transaction);
                    if (!isDummy)
                        txId = _web3.Eth.Transactions.SendRawTransaction.SendRequestAsync(transaction).Result;
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed send: " + ex.Message);
                }
                nonce++;
                Console.WriteLine("Sent with txid = " + txId);
            }
        }

        private List<SnapshotItem> LoadFromFile(string file)
        {
            string[] lines = File.ReadAllLines(file);
            List<SnapshotItem> items = new List<SnapshotItem>();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(new char[] { ',' });
                items.Add(new SnapshotItem(parts[0], decimal.Parse(parts[1])));
            }
            return items;
        }
    }
}