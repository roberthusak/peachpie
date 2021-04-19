using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pchp.Core
{
    public static class RuntimeTracing
    {
        public static StreamWriter TraceWriter { get; set; }

        private static bool _writeCommaBeforeParam;

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
            if (TraceWriter != null)
            {
                if (_writeCommaBeforeParam)
                {
                    TraceWriter.Write(", ");
                }

                TraceWriter.Write(value.ToStringUtf8());
            }

            _writeCommaBeforeParam = true;
        }

        public static void TraceRoutineCallParameters(PhpValue[] values)
        {
            foreach (var value in values)
            {
                TraceRoutineCallParameter(value);
            }
        }

        public static void TraceRoutineCallEnd()
        {
            TraceWriter?.WriteLine(')');

            _writeCommaBeforeParam = false;
        }
    }
}
