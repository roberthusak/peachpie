using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Pchp.Core;

namespace Benchmarks
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class PhpStringPassBenchmark
    {
        private int SubCallee(PhpValue value)
        {
            return (int)value.TypeCode;
        }

        private int PhpValueCallee(PhpValue value)
        {
            Operators.PassValue(ref value);
            return SubCallee(value);
        }

        private int StringCallee(string value)
        {
            return SubCallee((PhpValue)value);
        }

        [Benchmark(Baseline = true)]
        public int PassPhpValue() => PhpValueCallee("foo");

        [Benchmark]
        public int PassString() => StringCallee("foo");
    }
}
