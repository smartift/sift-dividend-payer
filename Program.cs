using System;
using Guytp.Config;

namespace Sift.DividendPayer
{
    class Program
    {
        static void Main(string[] args)
        {
            Menu menu = new Menu();
            while (!menu.ShouldExit)
            {
                menu.Display();
            }
        }
    }
}
