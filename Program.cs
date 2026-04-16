using System;
using System.Net.NetworkInformation;
using System.Threading;

class Program
{
    static void Main()
    {
        var target = ""jinom.net"";
        int rto = 0;
        int success = 0;
        for(int i = 0; i < 35; i++)
        {
            try {
                using var p = new Ping();
                var r = p.Send(target, 2000);
                if (r.Status == IPStatus.Success) success++;
                else rto++;
            } catch { rto++; }
            Thread.Sleep(500);
            Console.WriteLine($""{success} Ok, {rto} RTO"");
        }
    }
}
