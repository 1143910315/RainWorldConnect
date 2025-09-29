using RainWorldConnectTests.Benchmark;
using RainWorldConnectTests.Socket;

namespace RainWorldConnectBenchmark {
    internal class Program {
        static void Main(string[] args) {
            // new ProtocolParserBenchmark().TestMain();
            Task.Run(() => new TouchClient().CreateClientAsync()).Wait();
            while (true) {
                Thread.Sleep(1000);
            }
        }
    }
}
