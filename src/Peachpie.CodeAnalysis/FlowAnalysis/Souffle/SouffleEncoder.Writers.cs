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

                // Clean up all the relevant files from previous outputs (we would rewrite some of them anyway)
                foreach (var relation in SouffleUtils.ExportedProperties.Values)
                {
                    File.Delete(Path.Combine(basePath, relation.Name + FileSuffix));
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
                var writer = FindPropertyWriter(typeName, propertyName);
                writer.Write(parent);
                writer.Write('\t');
                writer.WriteLine(value);
            }

            public void WritePropertyItem(string typeName, string propertyName, string parent, int index, string value)
            {
                var writer = FindPropertyWriter(typeName, propertyName);
                writer.Write(parent);
                writer.Write('\t');
                writer.Write(index);
                writer.Write('\t');
                writer.WriteLine(value);
            }

            private TextWriter FindPropertyWriter(string typeName, string propertyName)
            {
                if (!_writers.TryGetValue((typeName, propertyName), out var writer))
                {
                    // Create the writer lazily
                    string relationName =
                        SouffleUtils.ExportedProperties
                        .Where(kvp => kvp.Key.Name == propertyName && kvp.Key.DeclaringType.Name == typeName)
                        .Single()
                        .Value.Name;

                    writer = new StreamWriter(File.Open(Path.Combine(_basePath, relationName + FileSuffix), FileMode.Create));
                    _writers[(typeName, propertyName)] = writer;
                }

                return writer;
            }
        }
    }
}
