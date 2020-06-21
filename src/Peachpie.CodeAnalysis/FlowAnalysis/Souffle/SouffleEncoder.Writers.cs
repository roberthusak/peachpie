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

            private readonly StreamWriter _routineNodeWriter;
            private readonly TextWriter _nextWriter;

            private readonly Dictionary<string, TextWriter> _typeWriters = new Dictionary<string, TextWriter>();

            private readonly Dictionary<(string, string), TextWriter> _propWriters = new Dictionary<(string, string), TextWriter>();

            public Writers(string basePath)
            {
                _basePath = basePath;

                // Writer of the RoutineNode relation
                _routineNodeWriter = new StreamWriter(File.Open(Path.Combine(_basePath, SouffleUtils.RoutineNodeRelation.Name + FileSuffix), FileMode.Create));

                // Writer of the Next relation
                _nextWriter = new StreamWriter(File.Open(Path.Combine(_basePath, SouffleUtils.NextRelation.Name + FileSuffix), FileMode.Create));

                // Create all the necessary type relation writers
                foreach (var kvp in SouffleUtils.ExportedTypeRelations.Where(t => !t.Key.IsAbstract))
                {
                    var writer = new StreamWriter(File.Open(Path.Combine(_basePath, kvp.Value.Name + FileSuffix), FileMode.Create));
                    _typeWriters[kvp.Key.Name] = writer;
                }

                // Create/erase all the property files in advance (even empty relations must exist) and open them for writing
                foreach (var kvp in SouffleUtils.ExportedProperties)
                {
                    var writer = new StreamWriter(File.Open(Path.Combine(_basePath, kvp.Value.Name + FileSuffix), FileMode.Create));
                    _propWriters[(kvp.Key.DeclaringType.Name, kvp.Key.Name)] = writer;
                }
            }

            public void Dispose()
            {
                _routineNodeWriter.Dispose();
                _nextWriter.Dispose();

                foreach (var writer in _typeWriters.Values)
                {
                    writer.Dispose();
                }

                foreach (var writer in _propWriters.Values)
                {
                    writer.Dispose();
                }
            }

            public void WriteRoutineNode(string routine, string node)
            {
                _routineNodeWriter.Write(routine);
                _routineNodeWriter.Write('\t');
                _routineNodeWriter.WriteLine(node);
            }

            public void WriteNext(string from, string to)
            {
                _nextWriter.Write(from);
                _nextWriter.Write('\t');
                _nextWriter.WriteLine(to);
            }

            public void WriteType(string typeName, string value)
            {
                _typeWriters[typeName].WriteLine(value);
            }

            public void WriteProperty(string typeName, string propertyName, string parent, string value)
            {
                var writer = _propWriters[(typeName, propertyName)];
                writer.Write(parent);
                writer.Write('\t');
                writer.WriteLine(value);
            }

            public void WritePropertyItem(string typeName, string propertyName, string parent, int index, string value)
            {
                var writer = _propWriters[(typeName, propertyName)];
                writer.Write(parent);
                writer.Write('\t');
                writer.Write(index);
                writer.Write('\t');
                writer.WriteLine(value);
            }
        }
    }
}
