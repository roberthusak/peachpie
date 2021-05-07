using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Benchmarks
{
    public class TypeCheckBenchmark
    {
        class C
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void O()
            {
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SimpleIsCallee(object o)
        {
            if (o is C)
            {
                ((C)o).O();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void IsPatternMatchCallee(object o)
        {
            if (o is C c)
            {
                c.O();
            }
        }

        [Benchmark(Baseline = true)]
        public void SimpleIs() => SimpleIsCallee(new C());

        [Benchmark]
        public void IsPatternMatch() => IsPatternMatchCallee(new C());
    }
}
