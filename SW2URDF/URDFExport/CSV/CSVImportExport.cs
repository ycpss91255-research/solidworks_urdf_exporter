using CsvHelper;
using log4net;
using Microsoft.VisualBasic.FileIO;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;

namespace SW2URDF.URDFExport.CSV
{
    /// <summary>
    /// Class to perform exporting to CSV and eventually importing of data from a CSV file
    /// </summary>
    public static class ImportExport
    {
        private static readonly ILog logger = Logger.GetLogger();

        #region Public Methods

        /// <summary>
        /// Method to write a full URDF robot to a CSV
        /// </summary>
        /// <param name="robot">URDF robot tree</param>
        /// <param name="filename">Fully qualified string name to write to</param>
        public static void WriteRobotToCSV(Robot robot, string filename)
        {
            logger.Info("Writing CSV file " + filename);
            using (StreamWriter stream = new StreamWriter(filename))
            {
                CsvWriter writer = new CsvWriter(stream, new CsvHelper.Configuration.Configuration { CultureInfo = CultureInfo.InvariantCulture });
                WriteHeaderToCSV(writer);
                WriteLinkToCSV(writer, robot.BaseLink);
            }
        }

        /// <summary>
        /// Loads a list of URDF Links from a CSV file
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static List<Link> LoadURDFRobotFromCSV(Stream stream)
        {
            return LoadURDFRobotFromCSV(stream, null);
        }

        /// <summary>
        /// Loads a list of URDF Links from a CSV file and says in the report what could not be read.
        /// The separator (comma, semicolon or tab) is detected from the header. Cells that are blank or
        /// not readable are left out of Link.CsvData, so a merge keeps the current value for them.
        /// </summary>
        /// <exception cref="CsvFormatException">There is no "Link Name" column.</exception>
        public static List<Link> LoadURDFRobotFromCSV(Stream stream, CsvLoadReport report)
        {
            report = report ?? new CsvLoadReport();
            string text;
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                text = reader.ReadToEnd();
            }

            List<StringDictionary> loadedFields = new List<StringDictionary>();
            string[] headers = null;
            foreach (char delimiter in new[] { ',', ';', '\t' })
            {
                headers = ReadHeader(text, delimiter);
                if (headers != null && headers.Any(h => h.Trim() == ContextToColumns.KeyName))
                {
                    report.Delimiter = delimiter;
                    break;
                }
                headers = null;
            }
            if (headers == null)
            {
                throw new CsvFormatException("The CSV file has no '" + ContextToColumns.KeyName + "' column");
            }
            headers = headers.Select(h => h.Trim()).ToArray();

            using (TextFieldParser csvParser = new TextFieldParser(new StringReader(text)))
            {
                csvParser.SetDelimiters(new string[] { report.Delimiter.ToString() });
                csvParser.ReadFields();
                int row = 1;
                while (!csvParser.EndOfData)
                {
                    string[] fields = csvParser.ReadFields();
                    row++;
                    StringDictionary dictionary = new StringDictionary();

                    int minArrayLength = Math.Min(fields.Length, headers.Length);
                    if (fields.Length != headers.Length)
                    {
                        logger.Warn(string.Format(
                            "The number of columns in the row do not match the number of columns in the header {0} != {1}",
                            fields.Length, headers.Length));
                    }
                    bool rowHasProblem = false;
                    for (int i = 0; i < minArrayLength; i++)
                    {
                        if (string.IsNullOrWhiteSpace(fields[i]))
                        {
                            continue;
                        }
                        if (IsNumericColumn(headers[i]) && !URDFAttribute.TryParseDouble(fields[i], out _))
                        {
                            report.Problems.Add(new CsvProblem
                            {
                                Kind = CsvProblemKind.NotANumber,
                                Row = row,
                                Column = headers[i],
                                Value = fields[i]
                            });
                            rowHasProblem = true;
                            continue;
                        }
                        dictionary[headers[i]] = fields[i];
                    }

                    if (string.IsNullOrWhiteSpace(dictionary[ContextToColumns.KeyName]))
                    {
                        if (dictionary.Count > 0 || rowHasProblem)
                        {
                            report.Problems.Add(new CsvProblem { Kind = CsvProblemKind.EmptyLinkName, Row = row });
                        }
                        continue;
                    }
                    loadedFields.Add(dictionary);
                }

                return loadedFields.Select(fields => BuildLinkFromData(fields)).ToList();
            }
        }

        private static string[] ReadHeader(string text, char delimiter)
        {
            using (TextFieldParser parser = new TextFieldParser(new StringReader(text)))
            {
                parser.SetDelimiters(new string[] { delimiter.ToString() });
                try
                {
                    return parser.EndOfData ? null : parser.ReadFields();
                }
                catch (MalformedLineException)
                {
                    return null;
                }
            }
        }

        // Columns whose cells are text. All other columns of the exporter CSV hold numbers.
        private static readonly string[] TextContextSuffixes =
            { ".name", ".filename", ".type", ".link", ".SWComponents", ".CoordSysName", ".AxisName" };

        private static bool IsNumericColumn(string columnName)
        {
            foreach (DictionaryEntry entry in ContextToColumns.Dictionary)
            {
                if ((string)entry.Value == columnName)
                {
                    string context = (string)entry.Key;
                    return !TextContextSuffixes.Any(suffix => context.EndsWith(suffix, StringComparison.Ordinal));
                }
            }
            return false;
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Iterates through the column names and writes them to a file stream
        /// </summary>
        /// <param name="stream">Stream to write to</param>
        private static void WriteHeaderToCSV(CsvWriter writer)
        {
            foreach (DictionaryEntry entry in ContextToColumns.Dictionary)
            {
                string columnName = (string)entry.Value;
                writer.WriteField(columnName);
            }
            writer.NextRecord();
        }

        /// <summary>
        /// Appends a line to an open CSV document of a URDF's Link properties
        /// </summary>
        /// <param name="stream">Stream representing opened CSV file</param>
        /// <param name="dictionary">Dictionary of values</param>
        private static void WriteValuesToCSV(CsvWriter writer, OrderedDictionary dictionary)
        {
            foreach (DictionaryEntry entry in ContextToColumns.Dictionary)
            {
                string context = (string)entry.Key;
                if (dictionary.Contains(context))
                {
                    object value = dictionary[context];
                    writer.WriteField(FormatValue(value));
                }
                else
                {
                    writer.WriteField(null);
                }
            }

            writer.NextRecord();
            StringBuilder missingColumns = new StringBuilder();
            foreach (string missing in UnwrittenColumns(dictionary.Keys.Cast<string>()))
            {
                missingColumns.Append(missing).Append(",");
            }
            if (missingColumns.Length > 0)
            {
                logger.Error("The following columns were not written to the CSV: " + missingColumns.ToString());
            }
        }

        // Numbers are written the way URDF writes them, whatever the culture of this machine
        private static string FormatValue(object value)
        {
            if (value is double number) return number.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // The child of a joint is the link itself, whose name is the "Link Name" column,
        // so this element deliberately has no column of its own.
        private const string ChildLinkContext = "Link.Joint.ChildLink.link";

        /// <summary>Keys of a link's values that have no CSV column and are not deliberately omitted.</summary>
        public static IEnumerable<string> UnwrittenColumns(IEnumerable<string> contexts)
        {
            HashSet<string> columns = new HashSet<string>(ContextToColumns.Dictionary.Keys.Cast<string>());
            columns.Add(ChildLinkContext);
            return contexts.Where(context => !columns.Contains(context)).ToList();
        }

        /// <summary>
        /// Converts a URDF Link to a dictionary of values and writes them to a CSV
        /// </summary>
        /// <param name="stream">StreamWriter of opened CSV document</param>
        /// <param name="link">URDF link to append to the file</param>
        private static void WriteLinkToCSV(CsvWriter writer, Link link)
        {
            OrderedDictionary dictionary = new OrderedDictionary();
            link.AppendToCSVDictionary(new List<string>(), dictionary);
            WriteValuesToCSV(writer, dictionary);

            foreach (Link child in link.Children)
            {
                WriteLinkToCSV(writer, child);
            }
        }

        private static Link BuildLinkFromData(StringDictionary dictionary)
        {
            StringDictionary contextDictionary = new StringDictionary();
            foreach (DictionaryEntry entry in ContextToColumns.Dictionary)
            {
                string context = (string)entry.Key;
                string columnName = (string)entry.Value;

                if (dictionary.ContainsKey(columnName))
                {
                    contextDictionary[context] = dictionary[columnName];
                }
            }

            Link link = new Link();
            link.SetElementFromData(new List<string>(), contextDictionary);
            link.CsvData = contextDictionary;
            return link;
        }

        #endregion Private Methods
    }
}