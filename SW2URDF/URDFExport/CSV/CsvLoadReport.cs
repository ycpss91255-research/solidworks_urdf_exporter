using System.Collections.Generic;

namespace SW2URDF.URDFExport.CSV
{
    public enum CsvProblemKind
    {
        /// <summary>A cell of a numeric column is not a number (for example "0,5" or "abc").</summary>
        NotANumber,

        /// <summary>A row has no link name.</summary>
        EmptyLinkName
    }

    /// <summary>A cell or row of a CSV file that could not be read. Row 1 is the header.</summary>
    public class CsvProblem
    {
        public CsvProblemKind Kind;
        public int Row;
        public string Column;
        public string Value;
    }

    /// <summary>What a load of a CSV file found besides the links: the separator used and the
    /// cells that were ignored because they could not be read.</summary>
    public class CsvLoadReport
    {
        public char Delimiter = ',';
        public readonly List<CsvProblem> Problems = new List<CsvProblem>();
    }

    /// <summary>The file cannot be used as an exporter CSV at all (no "Link Name" column).</summary>
    public class CsvFormatException : System.Exception
    {
        public CsvFormatException(string message) : base(message)
        {
        }
    }
}
