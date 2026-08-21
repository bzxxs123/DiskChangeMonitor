using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiskChangeMonitor.Import
{
    /// <summary>
    /// Streaming RFC-4180-style CSV parser: quoted fields, doubled-quote escapes,
    /// and CRLF / LF / CR row endings. Reads one character at a time so a 280 MB
    /// export never has to be buffered as a whole.
    /// </summary>
    public sealed class CsvFieldParser
    {
        private readonly TextReader _reader;
        private int _currentLine = 1;

        public CsvFieldParser(TextReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <summary>Physical 1-based line of the next character to be read.</summary>
        public int CurrentLine => _currentLine;

        public IEnumerable<string[]> ReadRows()
        {
            while (true)
            {
                var row = ReadRow();
                if (row == null)
                {
                    yield break;
                }

                yield return row;
            }
        }

        private string[]? ReadRow()
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var startLine = _currentLine;

            while (true)
            {
                var c = _reader.Read();
                if (c == -1)
                {
                    if (inQuotes)
                    {
                        throw new CsvParseException(startLine, "引号未闭合。");
                    }

                    if (fields.Count == 0 && field.Length == 0)
                    {
                        return null;
                    }

                    fields.Add(field.ToString());
                    return fields.ToArray();
                }

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (_reader.Peek() == '"')
                        {
                            _reader.Read();
                            field.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        if (c == '\n')
                        {
                            _currentLine++;
                        }

                        field.Append((char)c);
                    }

                    continue;
                }

                if (c == '"' && field.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    if (_reader.Peek() == '\n')
                    {
                        _reader.Read();
                    }

                    _currentLine++;
                    fields.Add(field.ToString());
                    return fields.ToArray();
                }
                else if (c == '\n')
                {
                    _currentLine++;
                    fields.Add(field.ToString());
                    return fields.ToArray();
                }
                else
                {
                    field.Append((char)c);
                }
            }
        }
    }
}
