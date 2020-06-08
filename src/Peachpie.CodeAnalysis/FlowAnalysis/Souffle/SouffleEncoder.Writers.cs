using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    partial class SouffleEncoder
    {
        private class Writers : IDisposable
        {
            private const string FileSuffix = ".facts";

            private readonly string _basePath;

            private readonly Dictionary<(string, string), TextWriter> _writers = new Dictionary<(string, string), TextWriter>();

            public Writers(string basePath)
            {
                _basePath = basePath;

                // Create/erase all the property files in advance (even empty relations must exist) and open them for writing
                foreach (var kvp in SouffleUtils.ExportedProperties)
                {
                    var writer = new StreamWriter(File.Open(Path.Combine(_basePath, kvp.Value.Name + FileSuffix), FileMode.Create));
                    _writers[(kvp.Key.DeclaringType.Name, kvp.Key.Name)] = writer;
                }
            }

            public void Dispose()
            {
                foreach (var writer in _writers.Values)
                {
                    writer.Dispose();
                }
            }

            public void WriteProperty(string typeName, string propertyName, string parent, string value)
            {
                var writer = _writers[(typeName, propertyName)];
                writer.Write(parent);
                writer.Write('\t');
                writer.WriteLine(value);
            }

            public void WritePropertyItem(string typeName, string propertyName, string parent, int index, string value)
            {
                var writer = _writers[(typeName, propertyName)];
                writer.Write(parent);
                writer.Write('\t');
                writer.Write(index);
                writer.Write('\t');
                writer.WriteLine(value);
            }
        }
    }
}
