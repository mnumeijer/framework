﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Formatters.Binary;
using Signum.Utilities.Properties;
using System.Globalization;
using System.Linq.Expressions;

namespace Signum.Utilities
{
    public static class StringExtensions
    {
        static Expression<Func<string, bool>> HasTextExpression = str => str != null && str != "";

        public static bool HasText(this string str)
        {
            return !string.IsNullOrEmpty(str);
        }

        public static string DefaultText(this string str, string defaultText)
        {
            if (str.HasText()) 
                return str;
            else 
                return defaultText;
        }

        public static string AssertHasText(this string str, string errorMessage)
        {
            if (str.HasText())
                return str;
            else
                throw new ArgumentException(errorMessage);
        }

        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source.IndexOf(toCheck, comp) >= 0;
        }

        public static string Add(this string str, string separator, string part)
        {
            if (str.HasText())
            {
                if (part.HasText())
                    return str + separator + part;
                else
                    return str;
            }
            else
                return part;
        }

        public static string AddLine(this string str, string part)
        {
            return Add(str, "\r\n", part);
        }

        public static string[] Lines(this string str)
        {
            if (str.HasText())
                return str.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            else
                return new string[0];
        }

        static InvalidOperationException NotFound(string str, char separator)
        {
            return new InvalidOperationException("Separator '{0}' not found on {1}".Formato(separator, str));
        }

        static InvalidOperationException NotFound(string str, string separator)
        {
            return new InvalidOperationException("Separator '{0}' not found on {1}".Formato(separator, str));
        }

        public static string Before(this string str, char separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(0, index);
        }

        public static string Before(this string str, string separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(0, index);
        }

        public static string After(this string str, char separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(index + 1);
        }

        public static string After(this string str, string separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(index + 1);
        }

        public static string TryBefore(this string str, char separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(0, index);
        }

        public static string TryBefore(this string str, string separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(0, index);
        }

        public static string TryAfter(this string str, char separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(index + 1);
        }

        public static string TryAfter(this string str, string separator)
        {
            int index = str.IndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(index + 1);
        }


        public static string BeforeLast(this string str, char separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(0, index);
        }

        public static string BeforeLast(this string str, string separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(0, index);
        }

        public static string AfterLast(this string str, char separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(index + 1);
        }

        public static string AfterLast(this string str, string separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                throw NotFound(str, separator);

            return str.Substring(index + 1);
        }

        public static string TryBeforeLast(this string str, char separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(0, index);
        }

        public static string TryBeforeLast(this string str, string separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(0, index);
        }

        public static string TryAfterLast(this string str, char separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(index + 1);
        }

        public static string TryAfterLast(this string str, string separator)
        {
            int index = str.LastIndexOf(separator);
            if (index == -1)
                return null;

            return str.Substring(index + 1);
        }




        public static string Between(this string str, string firstSeparator, string secondSeparator = null)
        {
            if (secondSeparator == null)
                secondSeparator = firstSeparator;

            int start = str.IndexOf(firstSeparator);
            if (start == -1)
               throw NotFound(str, firstSeparator);

            start = start + 1;

            int end = str.IndexOf(secondSeparator, start);
            if (start == -1)
                throw NotFound(str, secondSeparator);

            return str.Substring(start, end - start);
        }

        public static string Between(this string str, char firstSeparator, char secondSeparator = (char)0)
        {
            if (secondSeparator == 0)
                secondSeparator = firstSeparator;

            int start = str.IndexOf(firstSeparator);
            if (start == -1)
                throw NotFound(str, firstSeparator);

            start = start + 1;

            int end = str.IndexOf(secondSeparator, start);
            if (start == -1)
                throw NotFound(str, secondSeparator);

            return str.Substring(start, end - start);
        }

        public static string TryBetween(this string str, string firstSeparator, string secondSeparator = null)
        {
            if (secondSeparator == null)
                secondSeparator = firstSeparator;

            int start = str.IndexOf(firstSeparator);
            if (start == -1)
                return null;

            start = start + 1;

            int end = str.IndexOf(secondSeparator, start);
            if (start == -1)
                return null;

            return str.Substring(start, end - start);
        }

        public static string TryBetween(this string str, char firstSeparator, char secondSeparator = (char)0)
        {
            if (secondSeparator == 0)
                secondSeparator = firstSeparator;

            int start = str.IndexOf(firstSeparator);
            if (start == -1)
                return null;

            start = start + 1;

            int end = str.IndexOf(secondSeparator, start);
            if (start == -1)
                return null;

            return str.Substring(start, end - start);
        }

        public static string Start(this string str, int numChars)
        {
            if (numChars > str.Length)
                throw new InvalidOperationException("String '{0}' is too short".Formato(str));

            return str.Substring(0, numChars);
        }

        public static string TryStart(this string str, int numChars)
        {
            if (numChars > str.Length)
                return str;

            return str.Substring(0, numChars);
        }

        public static string End(this string str, int numChars)
        {
            if (numChars > str.Length)
                throw new InvalidOperationException("String '{0}' is too short".Formato(str));

            return str.Substring(str.Length - numChars, numChars);
        }

        public static string TryEnd(this string str, int numChars)
        {
            if (numChars > str.Length)
                return str;

            return str.Substring(str.Length - numChars, numChars);
        }

        public static string RemoveStart(this string str, int numChars)
        {
            if (numChars > str.Length)
                throw new InvalidOperationException("String '{0}' is too short".Formato(str));

            return str.Substring(numChars);
        }

        public static string TryRemoveStart(this string str, int numChars)
        {
            if (numChars > str.Length)
                return "";

            return str.Substring(numChars);
        }

        public static string RemoveEnd(this string str, int numChars)
        {
            if (numChars > str.Length)
                throw new InvalidOperationException("String '{0}' is too short".Formato(str));

            return str.Substring(0, str.Length - numChars);
        }

        public static string TryRemoveEnd(this string str, int numChars)
        {
            if (numChars > str.Length)
                return "";

            return str.Substring(0, str.Length - numChars);
        }

        public static string PadChopRight(this string str, int length)
        {
            str = str ?? "";
            return str.Length > length ? str.Substring(0, length) : str.PadRight(length);
        }

        public static string PadChopLeft(this string str, int length)
        {
            str = str ?? "";
            return str.Length > length ? str.Substring(str.Length - length, length) : str.PadLeft(length);
        }

        public static string VerticalEtc(this string str, int maxLines)
        {
            return str.VerticalEtc(maxLines, "(...)");
        }

        public static string VerticalEtc(this string str, int maxLines, string etcString)
        {
            if (str.HasText() && (str.Contains("\r\n")))
            {
                string[] arr = str.Split(new string[] { "\r\n" }, maxLines, StringSplitOptions.None);
                string res = arr.ToString("\r\n");
                if (res.Length < str.Length)
                    res += etcString;
                str = res;
            }
            return str;
        }

        public static string Etc(this string str, int max, string etcString)
        {
            if (str.HasText() && (str.Length > max))
                return str.Start(max - (etcString.HasText() ? etcString.Length : 0)) + etcString;
            return str;
        }

        public static string Etc(this string str, int max)
        {
            return str.Etc(max, "(...)");
        }

        public static string EtcLines(this string str, int max, string etcString)
        {
            if (!str.HasText())
                return str;

            int pos = str.IndexOfAny(new[] { '\n', '\r' });
            if (pos != -1 && pos + etcString.Length < max)
                max = pos + etcString.Length;

            if (str.HasText() && (str.Length > max))
                return str.Start(max - (etcString.HasText() ? etcString.Length : 0)) + etcString;
            return str;
        }

        public static string EtcLines(this string str, int max)
        {
            return str.EtcLines(max, "(...)");
        }

        public static bool ContinuesWith(this string str, string subString, int pos)
        {
            return str.IndexOf(subString, pos) == pos;
        }

        public static string RemoveChars(this string str, params char[] chars)
        {
            if (!str.HasText())
                return str;

            StringBuilder sb = new StringBuilder(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                if (!chars.Contains(str[i]))
                    sb.Append(str[i]);
            }
            return sb.ToString();
        }

        public static string Formato(string format, object arg0)
        {
            return string.Format(format, arg0);
        }

        public static string Formato(string format, object arg0, object arg1)
        {
            return string.Format(format, arg0, arg1);
        }

        public static string Formato(string format, object arg0, object arg1, object arg2)
        {
            return string.Format(format, arg0, arg1, arg2);
        }

        public static string Formato(this string pattern, params object[] parameters)
        {
            return string.Format(pattern, parameters);
        }

        public static string Formato(this string format, IFormatProvider provider, params object[] args)
        {
            return string.Format(provider, format, args);
        }

        public static string Replace(this string str, Dictionary<string, string> replacements)
        {
            return replacements.Aggregate(str, (s, kvp) => s.Replace(kvp.Key, kvp.Value));
        }

        public static string Indent(this string str, int numChars)
        {
            return Indent(str, numChars, ' ');
        }

        public static string Indent(this string str, int numChars, char indentChar)
        {
            string space = new string(indentChar, numChars);
            StringBuilder sb = new StringBuilder();
            using (StringReader sr = new StringReader(str))
            {
                for (string line = sr.ReadLine(); line != null; line = sr.ReadLine())
                {
                    sb.Append(space);
                    sb.AppendLine(line);
                }
            }

            string result = sb.ToString(0, str.EndsWith("\r\n") ? sb.Length : Math.Max(sb.Length - 2, 0));

            return result;
        }

        private static bool IsUpper(string pascalStr, int index)
        {
            if (index < 0)
                return false;

            if (index >= pascalStr.Length)
                return false;

            return !char.IsUpper(pascalStr[index]); 
        }

        public static string FirstUpper(this string str)
        {
            if (str.HasText())
                return char.ToUpper(str[0]) + str.Substring(1);
            return str;
        }

        public static string Replicate(this string str, int times)
        {
            if (times < 0)
                throw new ArgumentException("times");

            StringBuilder sb = new StringBuilder(str.Length * times);
            for (int i = 0; i < times; i++)
                sb.Append(str);
            return sb.ToString();
        }

        public static string Reverse(this string str)
        {
            char[] arr = new char[str.Length];
            int len = str.Length;
            for (int i = 0; i < len; i++)
                arr[i] = str[len - 1 - i];
            return new string(arr);
        }

        public static bool Wildcards(this string fileName, IEnumerable<string> wildcards)
        {
            return wildcards.Any(wc => fileName.Wildcards(wc));
        }

        static Dictionary<string, string> wildcardsPatterns = new Dictionary<string, string>();
        public static bool Wildcards(this string fileName, string wildcard)
        {
            var pattern = wildcardsPatterns.GetOrCreate(wildcard, wildcard.Replace(".", "[.]").Replace("*", ".*").Replace("?", "."));
            return Regex.IsMatch(fileName, pattern);
        }

        // like has an optional ESCAPE not available
        public static bool Like(this string str, string pattern)
        {
            pattern = Regex.Escape(pattern);
            pattern = pattern.Replace("%", ".*").Replace("_", ".");
            pattern = pattern.Replace(@"\[", "[").Replace(@"\]", "]").Replace(@"\^", "^");
            return Regex.IsMatch(str, pattern);
        }

        public static string RemoveDiacritics(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return s; 

            string normalizedString = s.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static string ToComputerSize(this long value)
        {
            return ToComputerSize(value, false);
        }

        static string[] abbreviations = new[] { "Bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string[] magnitudes = new[] { "Bytes", "KBytes", "MBytes", "GBytes", "TBytes", "PBytes", "EBytes", "ZBytes", "YBytes" };
        public static string ToComputerSize(this long value, bool useAbbreviations)
        {
            double valor = value;
            long i;
            for (i = 0; i < magnitudes.Length && valor >= 1024; i++)
                valor /= 1024.0;

            return "{0:#,###.00} {1}".Formato(valor, (useAbbreviations ? abbreviations : magnitudes)[i]);
        }


        public static string Combine(this string separator, params object[] elements)
        {
            StringBuilder sb = null;
            foreach (var item in elements)
            {
                if (item != null)
                {
                    if (sb == null)
                        sb = new StringBuilder();
                    else
                        sb.Append(separator);

                    sb.Append(item.ToString());
                }
            }

            return sb == null ? "" : sb.ToString();  // Remove at the end is faster
        }

        public static string CombineIfNotEmpty(this string separator, params object[] elements)
        {
            StringBuilder sb = null;
            foreach (var item in elements)
            {
                string str;
                if (item != null && (str = item.ToString()).HasText())
                {
                    if (sb == null)
                        sb = new StringBuilder();
                    else
                        sb.Append(separator);

                    sb.Append(str);
                }
            }

            return sb == null ? "" : sb.ToString();  // Remove at the end is faster
        }

        public static StringBuilder AppendLines(this StringBuilder sb, IEnumerable<string> strings)
        {
            foreach (var item in strings)
            {
                sb.AppendLine(item); 
            }

            return sb;
        }

        public static int CountRepetitions(this string text, string part)
        {
            int result = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(part, index);
                if (index == -1)
                    return result;

                index += part.Length;
                result++;
            }
        }
    }
}
