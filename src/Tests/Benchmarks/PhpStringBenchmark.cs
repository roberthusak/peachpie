using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Pchp.Core;

namespace Benchmarks
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class PhpStringBenchmark
    {
        readonly Context _ctx = Context.CreateEmpty();

        private void EchoPhpStringCallee(PhpString str)
        {
            str = new PhpString(str);
            _ctx.Echo(str);
        }

        private void EchoStringCallee(string str)
        {
            _ctx.Echo(str);
        }

        [Benchmark(Baseline = true)]
        public void EchoPhpString()
        {
            EchoPhpStringCallee((PhpString)"foo");
        }

        [Benchmark]
        public void EchoString()
        {
            EchoStringCallee("foo");
        }
    }
}
