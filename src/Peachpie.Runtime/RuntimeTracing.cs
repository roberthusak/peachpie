using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pchp.Core
{
    public static class RuntimeTracing
    {
        public static StreamWriter TraceWriter { get; set; }

        public static void TraceRoutineCallStart(string type, string routine)
        {
            if (TraceWriter != null)
            {
                TraceWriter.Write(type);
                TraceWriter.Write('.');
                TraceWriter.Write(routine);
                TraceWriter.Write('(');
            }
        }

        public static void TraceRoutineCallParameter(PhpValue value)
        {
            TraceWriter?.Write(value.ToStringUtf8());
        }

        public static void TraceRoutineCallParameters(PhpValue[] values)
        {
            if (TraceWriter != null)
            {
                foreach (var value in values)
                {
                    TraceWriter.Write(value.ToStringUtf8());
                }
            }
        }

        public static void TraceRoutineCallEnd()
        {
            TraceWriter?.WriteLine();
        }
    }
}
