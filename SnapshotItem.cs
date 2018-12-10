namespace Sift.DividendPayer
{
    public class SnapshotItem
    {
        public string Address { get; }

        public decimal Balance { get; set; }

        public SnapshotItem(string address, decimal balance)
        {
            Address = address;
            Balance = balance;
        }
    }
}