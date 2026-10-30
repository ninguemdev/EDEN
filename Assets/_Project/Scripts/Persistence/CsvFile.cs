using System;
using System.IO;
using System.Text;

namespace Eden.Persistence
{
    /// <summary>
    /// CSV file written row by row: UTF-8 without BOM, "\n" line ends, header first, and every row flushed so a crash
    /// keeps the data written so far. Rows are formatted by the caller with the invariant culture.
    /// </summary>
    internal sealed class CsvFile : IDisposable
    {
        private StreamWriter writer;
        private readonly bool flushRows;

        public CsvFile(string path, string header, bool flushEachRow = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            flushRows = flushEachRow;
            FilePath = path;
            writer = new StreamWriter(path, false, new UTF8Encoding(false), 65536) { NewLine = "\n" };
            writer.WriteLine(header);
            writer.Flush();
        }

        public string FilePath { get; }
        public int RowCount { get; private set; }

        public void Append(string row)
        {
            if (writer == null)
                throw new ObjectDisposedException(nameof(CsvFile));
            writer.WriteLine(row);
            if (flushRows) writer.Flush();
            RowCount++;
        }

        public void Flush() => writer?.Flush();

        public void Dispose()
        {
            writer?.Dispose();
            writer = null;
        }
    }
}
