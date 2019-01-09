using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sift.DividendPayer
{
    public class Menu
    {
        private BlockchainScanner _scanner = new BlockchainScanner();

        public bool ShouldExit { get; private set; }

        public MenuMode MenuMode { get; private set; }

        public void Display()
        {
            if (MenuMode == MenuMode.Root)
                DisplayRoot();
            else if (MenuMode == MenuMode.ScanBlockchain)
                DisplayScanBlockchainMenu();
            else if (MenuMode == MenuMode.MergeSnapshotFiles)
                DisplayMergeSnapshotFiles();
            else if (MenuMode == MenuMode.PayDividends)
                DisplayPayDividends();
        }

        private void DisplayRoot()
        {
            Console.WriteLine("Please select option:");
            Console.WriteLine("   1) Scan Blockchain");
            Console.WriteLine("   2) Pay Dividends");
            Console.WriteLine("   3) Merge Snapshot Files");
            Console.WriteLine("   0) Exit");
            Console.Write("Choice: ");
            char c = Console.ReadKey().KeyChar;
            Console.WriteLine();
            switch (c)
            {
                case '0':
                    ShouldExit = true;
                    break;
                case '1':
                    MenuMode = MenuMode.ScanBlockchain;
                    break;
                case '2':
                    MenuMode = MenuMode.PayDividends;
                    break;
                case '3':
                    MenuMode = MenuMode.MergeSnapshotFiles;
                    break;
                default:
                    Console.WriteLine("Invalid selection");
                    break;
            }
            if (c == '0')
            {
                ShouldExit = true;
                return;
            }
        }

        private void DisplayScanBlockchainMenu()
        {
            MenuMode = MenuMode.Root;
            Console.WriteLine("Scan Blockchain");
            DateTime suggested = GetDefaultDate();
            DateTime scanDate;
            while (true)
            {
                Console.Write("    Timestamp (GMT): [" + suggested.ToString("yyyy-MM-dd HH:mm:ss") + "] ");
                string readDate = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(readDate))
                {
                    scanDate = suggested;
                    break;
                }
                if (DateTime.TryParse(readDate, out scanDate))
                {
                    if (scanDate > DateTime.UtcNow)
                        Console.WriteLine("Date is in future, cannot snapshot yet");
                    else
                        break;
                }
                else
                    Console.WriteLine("Invalid date format");
            }
            string defaultFile = GetDefaultFilename(scanDate);
            string outputFile;
            Console.Write("    Filename: [" + defaultFile + "] ");
            string readFile = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(readFile))
                outputFile = defaultFile;
            else
                outputFile = readFile;

            // Perform the scan
            List<SnapshotItem> items;
            try
            {
                items = _scanner.Scan(scanDate, outputFile);
                Console.WriteLine("Saved to disk as " + outputFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to scan blockchain: " + ex.Message + Environment.NewLine + ex.ToString());
                return;
            }

            // Output the summary
            Console.WriteLine("Successfully scanned blockchain");
            Console.WriteLine("Holders:   " + items.Count);
            Console.WriteLine("Total:     " + items.Sum(i => i.Balance));

            // Save to disk
            OutputItems(items, outputFile);
        }

        private void OutputItems(List<SnapshotItem> items, string outputFile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Address,Balance");
            foreach (SnapshotItem item in items)
            {
                sb.Append(item.Address);
                sb.Append(",");
                sb.AppendLine(item.Balance.ToString());
            }
            try
            {
                File.WriteAllText(outputFile, sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to output to disk at \"" + outputFile + "\": " + ex.Message);
                Console.Write("Press any key to display... ");
                Console.ReadKey();
                Console.WriteLine();
                Console.WriteLine(sb.ToString());
            }
        }

        private static DateTime GetDefaultDate()
        {
            DateTime now = DateTime.UtcNow;
            DateTime d = new DateTime(now.Year, now.Month, now.Day, 10, 0, 0, DateTimeKind.Utc);
            if (d > DateTime.UtcNow)
                d = d.AddDays(-1);
            return d;
        }

        private static string GetDefaultFilename(DateTime scanDate)
        {
            return "sift-snapshot-" + scanDate.ToString("yyyy-MM-dd_HHmmss") + ".csv";
        }

        private void DisplayPayDividends()
        {
            MenuMode = MenuMode.Root;
            string file = null;
            string privateKey = null;
            decimal amountToSend = 0;
            ulong? startNonce = null;
            string defaultFile = GetDefaultFilename(GetDefaultDate());
            Console.Write("Input File [" + defaultFile + "]: ");
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                file = defaultFile;
            else
                file = input;
            while (true)
            {
                Console.Write("Private Key: ");
                input = Console.ReadLine();
                privateKey = input;
                if (!string.IsNullOrEmpty(input))
                    break;
            }
            while (true)
            {
                Console.Write("Amount to Send? ");
                input = Console.ReadLine();
                if (decimal.TryParse(input, out amountToSend))
                    break;
            }
            while (true)
            {
                Console.Write("Start nonce? ");
                input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    startNonce = null;
                    break;
                }
                ulong n;
                if (ulong.TryParse(input, out n))
                {
                    startNonce = n;
                    break;
                }
            }

            // Do the sending
            TransactionSender sender = new TransactionSender();
            sender.ProcessTransactions(file, privateKey, amountToSend, startNonce);
        }

        private void DisplayMergeSnapshotFiles()
        {
            MenuMode = MenuMode.Root;
            string file1 = null;
            string file2 = null;
            string outputFile = null;
            while (true)
            {
                Console.Write("File 1: ");
                file1 = Console.ReadLine();
                if (string.IsNullOrEmpty(file1) || !File.Exists(file1))
                    Console.WriteLine("File not found: " + file1);
                else
                    break;
            }
            while (true)
            {
                Console.Write("File 2: ");
                file2 = Console.ReadLine();
                if (string.IsNullOrEmpty(file2) || !File.Exists(file2))
                    Console.WriteLine("File not found: " + file2);
                else
                    break;
            }
            while (true)
            {
                Console.Write("Output: ");
                outputFile = Console.ReadLine();
                if (string.IsNullOrEmpty(outputFile))
                    Console.WriteLine("File not specified");
                else
                    break;
            }
            string[] lines1 = File.ReadAllLines(file1);
            string[] lines2 = File.ReadAllLines(file2);
            List<SnapshotItem> items = new List<SnapshotItem>();
            for (int i = 1; i < lines1.Length; i++)
            {
                string[] parts = lines1[i].Split(new char[] { ',' });
                string address = parts[0];
                decimal balance = decimal.Parse(parts[1]);
                items.Add(new SnapshotItem(address, balance));
            }
            for (int i = 1; i < lines2.Length; i++)
            {
                string[] parts = lines2[i].Split(new char[] { ',' });
                string address = parts[0];
                decimal balance = decimal.Parse(parts[1]);
                SnapshotItem existing = items.FirstOrDefault(it => it.Address == address);
                if (existing == null)
                    items.Add(new SnapshotItem(address, balance));
                else
                    existing.Balance += balance;
            }
            OutputItems(items, outputFile);
        }
    }
}