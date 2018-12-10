using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace Sift.DividendPayer
{
    /// <summary>
    /// This class describes the transfer of an ERC20 token.
    /// </summary>
    [EventAttribute("transfer")]
    internal class Erc20TransferEvent
    {
        #region Properties
        /// <summary>
        /// Gets who the transfer is from.
        /// </summary>
        [Parameter("address", "from", 1, true)]
        public string From { get; set; }

        /// <summary>
        /// Gets who the transfer is to.
        /// </summary>
        [Parameter("address", "to", 2, true)]
        public string To { get; set; }

        /// <summary>
        /// Gets the value of the transfer.
        /// </summary>
        [Parameter("uint256", "value", 3)]
        public BigInteger Value { get; set; }
        #endregion
    }
}