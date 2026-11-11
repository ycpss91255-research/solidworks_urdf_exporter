using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.Serialization;
using System.Xml;

namespace SW2URDF.URDF
{
    [DataContract(IsReference = true, Name = "Attribute", Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class URDFAttribute
    {
        public static readonly NumberFormatInfo URDFNumberFormat =
            CultureInfo.InvariantCulture.NumberFormat;

        // Digits, a sign, one decimal point and an exponent. No thousands separators or currency symbols: "1,5" is
        // not a number, and must not be read as 15.
        public static readonly NumberStyles URDFNumberStyle = NumberStyles.Float;

        [DataMember]
        private bool IsRequired;

        [DataMember]
        private readonly string AttributeType;

        [DataMember]
        public object Value;

        public URDFAttribute(string type, bool isRequired, object initialValue)
        {
            AttributeType = type;
            IsRequired = isRequired;
            Value = initialValue;
        }

        public void WriteURDF(XmlWriter writer)
        {
            string valueString = "";
            if (Value.GetType() == typeof(double[]))
            {
                double[] valueArray = (double[])Value;
                foreach (double d in valueArray)
                {
                    valueString +=
                        d.ToString(URDFNumberFormat) + " ";
                }
                valueString = valueString.Trim();
            }
            else if (Value.GetType() == typeof(double))
            {
                valueString =
                    ((Double)Value).ToString(URDFNumberFormat);
            }
            else if (Value.GetType() == typeof(string))
            {
                valueString = (string)Value;
            }
            else if (Value != null)
            {
                throw new Exception("Unhandled object type in write attribute");
            }
            if (IsRequired && Value == null)
            {
                throw new Exception("Required attribute " + AttributeType + " has null value");
            }
            if (String.IsNullOrWhiteSpace(AttributeType))
            {
                throw new Exception("No type specified");
            }
            if (Value != null)
            {
                writer.WriteAttributeString(AttributeType, valueString);
            }
        }

        public void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            if (Value.GetType() == typeof(double[]))
            {
                double[] values = (double[])Value;
                for (int i = 0; i < values.Length; i++)
                {
                    string contextString = string.Join(".", context) + "." + AttributeType + "." + AttributeType[i];
                    dictionary.Add(contextString, values[i]);
                }
            }
            else
            {
                string contextString = string.Join(".", context) + "." + AttributeType;
                dictionary.Add(contextString, Value);
            }
        }

        public void SetValueFromData(List<string> context, StringDictionary dictionary)
        {
            string contextString = string.Join(".", context) + "." + AttributeType;
            if (dictionary.ContainsKey(contextString))
            {
                SetValueFromText(contextString, dictionary[contextString]);
            }
        }

        // Attributes whose value is text even when it is empty or not set yet
        private static readonly HashSet<string> TextAttributeTypes =
            new HashSet<string> { "name", "type", "link", "filename", "joint" };

        /// <summary>
        /// Sets the value from a CSV field according to what this attribute holds: text stays text (also
        /// when it looks like a number or contains ';'), numbers are read with the invariant culture.
        /// A field that is not a valid number is logged and ignored; the previous value stays.
        /// </summary>
        private void SetValueFromText(string column, string text)
        {
            if (text == null)
            {
                return;
            }
            if (Value is string || (Value == null && TextAttributeTypes.Contains(AttributeType)))
            {
                Value = text;
            }
            else if (Value is double[])
            {
                string[] parts = text.Split(';');
                double[] numbers = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!TryParseDouble(parts[i], out numbers[i]))
                    {
                        Utilities.Logger.GetLogger().Warn("Ignoring CSV field " + column + ": '" + text + "' is not a list of numbers");
                        return;
                    }
                }
                Value = numbers;
            }
            else if (TryParseDouble(text, out double number))
            {
                Value = number;
            }
            else
            {
                Utilities.Logger.GetLogger().Warn("Ignoring CSV field " + column + ": '" + text + "' is not a number");
            }
        }

        /// <summary>Reads a number the way the URDF and the CSV files write it: invariant culture, no
        /// thousands separators.</summary>
        public static bool TryParseDouble(string text, out double value)
        {
            return Double.TryParse(text, URDFNumberStyle, URDFNumberFormat, out value);
        }

        public bool GetIsRequired()
        {
            return IsRequired;
        }

        public void SetRequired(bool required)
        {
            IsRequired = required;
        }

        public bool IsSet()
        {
            return Value != null;
        }

        public string GetTextFromDoubleValue(string format = "G")
        {
            string result = "";
            if (Value != null && Value.GetType() == typeof(double))
            {
                double dValue = (double)Value;
                result = dValue.ToString(format, URDFNumberFormat);
            }
            return result;
        }

        public string[] GetTextArrayFromDoubleArray(string format = "G")
        {
            string[] result = null;
            if (Value != null)
            {
                double[] dArray = (double[])Value;
                result = new string[dArray.Length];
                for (int i = 0; i < dArray.Length; i++)
                {
                    result[i] = dArray[i].ToString(format, URDFNumberFormat);
                }
            }
            return result;
        }

        public void SetDoubleValueFromString(string text)
        {
            if (Double.TryParse(text, URDFNumberStyle, URDFNumberFormat, out double result))
            {
                Value = result;
            }
            else if (GetIsRequired())
            {
                if (!String.IsNullOrWhiteSpace(text))
                {
                    Utilities.Logger.GetLogger().Warn("'" + text + "' is not a number for " + AttributeType + "; using 0");
                }
                Value = 0.0;
            }
        }

        public void SetDoubleArrayFromStringArray(string[] textArray)
        {
            double[] dArray = new double[textArray.Length];
            for (int i = 0; i < textArray.Length; i++)
            {
                if (Double.TryParse(textArray[i], URDFNumberStyle, URDFNumberFormat, out double result))
                {
                    dArray[i] = result;
                }
                else if (GetIsRequired())
                {
                    dArray[i] = 0.0;
                }
            }
            Value = dArray;
        }
    }
}