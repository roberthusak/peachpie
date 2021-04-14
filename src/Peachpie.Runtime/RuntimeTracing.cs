using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pchp.Core
{
    public static class RuntimeTracing
    {
        public static StreamWriter TraceWriter { get; set; }

        public static void TraceRoutineCall(string routine)
        {
            TraceWriter?.WriteLine(routine);
        }
    }
}
