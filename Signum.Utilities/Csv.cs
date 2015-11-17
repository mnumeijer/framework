﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Signum.Utilities.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;

namespace Signum.Utilities
{
    public static class Csv
    {
        public static Encoding DefaultEncoding = Encoding.GetEncoding(1252);

        public static string ToCsvFile<T>(this IEnumerable<T> collection, string fileName, Encoding encoding = null, CultureInfo culture = null, bool writeHeaders = true, bool autoFlush = false, bool append = false,
            Func<CsvColumnInfo<T>, CultureInfo, Func<object, string>> toStringFactory = null)
        {
            using (FileStream fs = append ? new FileStream(fileName, FileMode.Append, FileAccess.Write) : File.Create(fileName))
                ToCsv<T>(collection, fs, encoding, culture, writeHeaders, autoFlush, toStringFactory);

            return fileName;
        }

        public static byte[] ToCsvBytes<T>(this IEnumerable<T> collection, Encoding encoding = null, CultureInfo culture = null, bool writeHeaders = true, bool autoFlush = false,
            Func<CsvColumnInfo<T>, CultureInfo, Func<object, string>> toStringFactory = null)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                collection.ToCsv(ms, encoding, culture, writeHeaders, autoFlush, toStringFactory);
                return ms.ToArray();
            }
        }

        public static void ToCsv<T>(this IEnumerable<T> collection, Stream stream, Encoding encoding = null, CultureInfo culture = null, bool writeHeaders = true, bool autoFlush = false,
            Func<CsvColumnInfo<T>, CultureInfo, Func<object, string>> toStringFactory = null)
        {
            encoding = encoding ?? DefaultEncoding;
            culture = culture ?? CultureInfo.CurrentCulture;

            string separator = culture.TextInfo.ListSeparator;

            var columns = ColumnInfoCache<T>.Columns;
            var members = columns.Select(c => c.MemberEntry).ToList();
            var toString = columns.Select(c => GetToString(culture, c, toStringFactory)).ToList();

            using (StreamWriter sw = new StreamWriter(stream, encoding) { AutoFlush = autoFlush })
            {
                if (writeHeaders)
                    sw.WriteLine(members.ToString(m => HandleSpaces(m.Name), separator));

                foreach (var item in collection)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var obj = members[i].Getter(item); 
                        
                        var str = EncodeCsv(toString[i](obj), culture);

                        sw.Write(str);
                        if(i < members.Count - 1)
                            sw.Write(separator);
                        else
                            sw.WriteLine();
                    }
                }
            }
        }

        static string EncodeCsv(string p, CultureInfo culture)
        {
            if (p == null)
                return null;

            string separator = culture.TextInfo.ListSeparator;

            if (p.Contains(separator) || p.Contains("\"") || p.Contains("\r") || p.Contains("\n"))
            {
                return "\"" + p.Replace("\"", "\"\"") + "\"";
            }
            return p;
        }

        private static Func<object, string> GetToString<T>(CultureInfo culture, CsvColumnInfo<T> column, Func<CsvColumnInfo<T>, CultureInfo, Func<object, string>> toStringFactory)
        {
            if (toStringFactory != null)
            {
                var result = toStringFactory(column, culture);

                if (result != null)
                    return result;
            }

            return obj => ConvertToString(obj, column.Format, culture);
        }

        static string ConvertToString(object obj, string format, CultureInfo culture)
        {
            if (obj == null)
                return "";

            IFormattable f = obj as IFormattable;
            if (f != null)
                return f.ToString(null, culture);
            else
                return obj.ToString();
        }

        static string HandleSpaces(string p)
        {
            return p.Replace("__", "^").Replace("_", " ").Replace("^", "_");
        }

        public static List<T> ReadFile<T>(string fileName, Encoding encoding = null, CultureInfo culture = null, int skipLines = 1,
            Func<CsvColumnInfo<T>, CultureInfo, Func<string, object>> parserFactory = null) where T : new()
        {
            encoding = encoding ?? DefaultEncoding;
            culture = culture ?? CultureInfo.CurrentCulture;

            using (FileStream fs = File.OpenRead(fileName))
                return ReadStream<T>(fs, encoding, culture, skipLines, parserFactory).ToList();
        }

        public static List<T> ReadBytes<T>(byte[] data, Encoding encoding = null, CultureInfo culture = null, int skipLines = 1,
            Func<CsvColumnInfo<T>, CultureInfo, Func<string, object>> parserFactory = null) where T : new()
        {
            using (MemoryStream ms = new MemoryStream(data))
                return ReadStream<T>(ms, encoding, culture, skipLines, parserFactory).ToList();
        }

        public static IEnumerable<T> ReadStream<T>(Stream stream, Encoding encoding = null, CultureInfo culture = null, int skipLines = 1,
            Func<CsvColumnInfo<T>, CultureInfo, Func<string, object>> parserFactory = null)
            where T : new()
        {
            encoding = encoding ?? DefaultEncoding;
            culture = culture ?? CultureInfo.CurrentCulture;

            var columns = ColumnInfoCache<T>.Columns;
            var members = columns.Select(c => c.MemberEntry).ToList();
            var parsers =  columns.Select(c => GetParser(culture, c, parserFactory)).ToList();

            Regex regex = GetRegex(culture);

            using (StreamReader sr = new StreamReader(stream, encoding))
            {
                string str = sr.ReadToEnd();

                var matches = regex.Matches(str).Cast<Match>();

                if (skipLines > 0)
                    matches = matches.Skip(skipLines);

                foreach (var m in matches)
                {
                    if (m.Length > 0)
                    {
                        T t = ReadObject<T>(m, members, parsers);

                        yield return t;
                    }
                }
            }
        }

        public static T ReadLine<T>(string csvLine, CultureInfo culture = null, Func<CsvColumnInfo<T>, CultureInfo, Func<string, object>> parserFactory = null)
            where T : new()
        {
            culture = culture ?? CultureInfo.CurrentCulture;

            Regex regex = GetRegex(culture);

            Match m = regex.Match(csvLine);

            var columns = ColumnInfoCache<T>.Columns;

            return ReadObject<T>(m,
                columns.Select(c => c.MemberEntry).ToList(),
                columns.Select(c => GetParser(culture, c, parserFactory)).ToList());
        }

        private static Func<string, object> GetParser<T>(CultureInfo culture, CsvColumnInfo<T> column, Func<CsvColumnInfo<T>, CultureInfo, Func<string, object>> parserFactory)
        {
            if (parserFactory != null)
            {
                var result = parserFactory(column, culture);

                if (result != null)
                    return result;
            }

            return str => ConvertTo(str, column.MemberInfo.ReturningType(), culture, column.Format);
        }

        static T ReadObject<T>(Match m, List<MemberEntry<T>> members, List<Func<string, object>> parsers) where T : new()
        {
            var vals = m.Groups["val"].Captures;

            if (vals.Count < members.Count)
                throw new FormatException("Only {0} coulumns found (instead of {1}) in line: ".FormatWith(vals.Count, members.Count, m.Value));

            T t = new T();
            for (int i = 0; i < members.Count; i++)
            {
                string str = DecodeCsv(vals[i].Value);

                object val = parsers[i](str);

                members[i].Setter(t, val);

            }
            return t;
        }

     

        static ConcurrentDictionary<char, Regex> regexCache = new ConcurrentDictionary<char, Regex>();
        const string BaseRegex = @"^((?<val>'(?:[^']+|'')*'|[^;\r\n]*))?((?!($|\r\n));(?<val>'(?:[^']+|'')*'|[^;\r\n]*))*($|\r\n)";
        static Regex GetRegex(CultureInfo culture)
        {
            char separator = culture.TextInfo.ListSeparator.SingleEx();

            return regexCache.GetOrAdd(separator, s =>
                new Regex(BaseRegex.Replace('\'', '"').Replace(';', s), RegexOptions.Multiline | RegexOptions.ExplicitCapture));
        }

        static class ColumnInfoCache<T>
        {
            public static List<CsvColumnInfo<T>> Columns = MemberEntryFactory.GenerateList<T>(MemberOptions.Fields | MemberOptions.Properties | MemberOptions.Typed | MemberOptions.Setters | MemberOptions.Getter)
                .Select((me, i) => new CsvColumnInfo<T>(i, me, me.MemberInfo.GetCustomAttribute<FormatAttribute>().Try(f => f.Format))).ToList();
        }

        static string DecodeCsv(string s)
        {
            if (s.StartsWith("\""))
            {
                if (!s.EndsWith("\""))
                    throw new FormatException("Cell starts by quotes but not ends with quotes".FormatWith(s));

                string str = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");

                return Regex.Replace(str, "(?<!\r)\n", "\r\n");
            }

            if (s.Contains("\""))
                throw new FormatException("Cell has quotes ina unexpected position".FormatWith(s));

            return s;
        }

        static object ConvertTo(string s, Type type, CultureInfo culture, string format)
        {
            Type baseType = Nullable.GetUnderlyingType(type);
            if (baseType != null)
            {
                if (!s.HasText()) 
                    return null;

                type = baseType;
            }

            if (type.IsEnum)
                return Enum.Parse(type, s);

            if (type == typeof(DateTime))
                if (format == null)
                    return DateTime.Parse(s, culture);
                else
                    return DateTime.ParseExact(s, format, culture);

            return Convert.ChangeType(s, type, culture);
        }
    }


    public class CsvColumnInfo<T>
    {
        public readonly int Index;
        public readonly MemberEntry<T> MemberEntry;
        public readonly string Format;

        public MemberInfo MemberInfo
        {
            get { return this.MemberEntry.MemberInfo; }
        }

        internal CsvColumnInfo(int index, MemberEntry<T> memberEntry, string format)
        {
            this.Index = index;
            this.MemberEntry = memberEntry;
            this.Format = format;
        }
    }
}