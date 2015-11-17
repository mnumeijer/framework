﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Signum.Entities.Reflection;
using Signum.Utilities;
using Signum.Utilities.ExpressionTrees;
using Signum.Utilities.Reflection;
using System.Globalization;
using Signum.Entities.Basics;
using System.IO;

namespace Signum.Entities
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public abstract class ValidatorAttribute : Attribute
    {
        public Func<ModifiableEntity, bool> IsApplicable; 
        public Func<string> ErrorMessage { get; set; }

        public string UnlocalizableErrorMessage
        {
            get { return ErrorMessage == null ? null : ErrorMessage(); }
            set { ErrorMessage = () => value; }
        }

        public int Order { get; set; }

        //Descriptive information that continues the sentence: The property should {HelpMessage}
        //Used for documentation purposes only
        public abstract string HelpMessage { get; }

        public string Error(ModifiableEntity entity, PropertyInfo property, object value)
        {
            if (IsApplicable != null && !IsApplicable(entity))
                return null;

            string defaultError = OverrideError(value);

            if (defaultError == null)
                return null;

            string error = ErrorMessage == null ? defaultError : ErrorMessage();
            if (error != null)
                error = error.FormatWith(property.NiceName());

            return error; 
        }


        /// <summary>
        /// When overriden, validates the value against this validator rule
        /// </summary>
        /// <param name="value"></param>
        /// <returns>returns an string with the error message, using {0} if you want the property name to be inserted</returns>
        protected abstract string OverrideError(object value);
    }

    public class NotNullValidatorAttribute : ValidatorAttribute
    {
        protected override string OverrideError(object obj)
        {
            if (obj == null)
                return ValidationMessage._0IsNotSet.NiceToString();

            return null;
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.BeNotNull.NiceToString(); }
        }
    }

    public class StringLengthValidatorAttribute : ValidatorAttribute
    {
        public bool AllowNulls { get; set; }

        bool? allowLeadingSpaces;
        public bool AllowLeadingSpaces
        {
            get { return allowLeadingSpaces ?? MultiLine; }
            set { this.allowLeadingSpaces = value; }
        }

        bool? allowTrailingSpaces;
        public bool AllowTrailingSpaces
        {
            get { return allowTrailingSpaces ?? MultiLine; }
            set { this.allowTrailingSpaces = value; }
        }

        public bool MultiLine { get; set; }

        int min = -1;
        public int Min
        {
            get { return min; }
            set { min = value; }
        }

        int max = -1;
        public int Max
        {
            get { return max; }
            set { max = value; }
        }

        protected override string OverrideError(object value)
        {
            string val = (string)value;

            if (string.IsNullOrEmpty(val))
                return AllowNulls ? null : ValidationMessage._0IsNotSet.NiceToString();

            if(!MultiLine && (val.Contains('\n') || val.Contains('\r')))
                return ValidationMessage._0ShouldNotHaveBreakLines.NiceToString();

            if (!AllowLeadingSpaces && Regex.IsMatch(val, @"^\s+"))
                return ValidationMessage._0ShouldNotHaveInitialSpaces.NiceToString();

             if (!AllowLeadingSpaces && Regex.IsMatch(val, @"\s+$"))
                return ValidationMessage._0ShouldNotHaveFinalSpaces.NiceToString();

            if (min == max && min != -1 && val.Length != min)
                return ValidationMessage.TheLenghtOf0HasToBeEqualTo1.NiceToString("{0}", min);

            if (min != -1 && val.Length < min)
                return ValidationMessage.TheLengthOf0HasToBeGreaterOrEqualTo1.NiceToString("{0}", min);

            if (max != -1 && val.Length > max)
                return ValidationMessage.TheLengthOf0HasToBeLesserOrEqualTo1.NiceToString("{0}", max);

            return null;
        }

        public override string HelpMessage
        {
            get
            {
                string result =
                    min != -1 && max != -1 ? ValidationMessage.HaveBetween0And1Characters.NiceToString().FormatWith(min, max) :
                    min != -1 ? ValidationMessage.HaveMinimum0Characters.NiceToString().FormatWith(min) :
                    max != -1 ? ValidationMessage.HaveMaximum0Characters.NiceToString().FormatWith(max) : null;

                if (AllowNulls)
                    result = result.Add(" ", ValidationMessage.OrBeNull.NiceToString());

                return result;
            }
        }
    }


    public abstract class RegexValidatorAttribute : ValidatorAttribute
    {
        Regex regex;
        public RegexValidatorAttribute(Regex regex)
        {
            this.regex = regex;
        }

        public RegexValidatorAttribute(string regexExpresion)
        {
            this.regex = new Regex(regexExpresion);
        }

        public abstract string FormatName
        {
            get;
        }

        protected override string OverrideError(object value)
        {
            string str = (string)value;
            if (string.IsNullOrEmpty(str))
                return null;

            if (regex.IsMatch(str))
                return null;

            return ValidationMessage._0DoesNotHaveAValid1Format.NiceToString().FormatWith("{0}", FormatName);
        }

        public override string HelpMessage
        {
            get
            {
                return ValidationMessage.HaveValid0Format.NiceToString().FormatWith(FormatName);
            }
        }
    }

    public class EMailValidatorAttribute : RegexValidatorAttribute
    {
        public static readonly Regex EmailRegex = new Regex(
                          @"^(([^<>()[\]\\.,;:\s@\""]+"
                        + @"(\.[^<>()[\]\\.,;:\s@\""]+)*)|(\"".+\""))@"
                        + @"((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}"
                        + @"\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+"
                        + @"[a-zA-Z]{2,}))$", RegexOptions.IgnoreCase);

        public EMailValidatorAttribute()
            : base(EmailRegex)
        {
        }

        public override string FormatName
        {
            get { return "e-Mail"; }
        }
    }

    public class TelephoneValidatorAttribute : RegexValidatorAttribute
    {
        public static readonly Regex TelephoneRegex = new Regex(@"^((\+|00)\d\d)? *(\([ 0-9]+\))? *[0-9][ \-\.0-9]+$");

        public TelephoneValidatorAttribute()
            : base(TelephoneRegex)
        {
        }

        public override string FormatName
        {
            get { return ValidationMessage.Telephone.NiceToString(); }
        }
    }

    public class MultipleTelephoneValidatorAttribute : RegexValidatorAttribute
    {
        public static readonly Regex MultipleTelephoneRegex = new Regex(@"^((\+|00)\d\d)? *(\([ 0-9]+\))? *[0-9][ \-\.0-9]+(,\s*((\+|00)\d\d)? *(\([ 0-9]+\))? *[0-9][ \-\.0-9]+)*");

        public MultipleTelephoneValidatorAttribute()
            : base(MultipleTelephoneRegex)
        {
        }

        public override string FormatName
        {
            get { return ValidationMessage.Telephone.NiceToString(); }
        }
    }

    public class NumericTextValidatorAttribute : RegexValidatorAttribute
    {
        public static readonly Regex NumericTextRegex = new Regex(@"^[0-9]*$");

        public NumericTextValidatorAttribute()
            : base(NumericTextRegex)
        {
        }

        public override string FormatName
        {
            get { return ValidationMessage.Numeric.NiceToString(); }
        }
    }

    public class URLValidatorAttribute : RegexValidatorAttribute
    {
        public static readonly Regex URLRegex = new Regex(
              "^(https?://)"
            + "?(([0-9a-z_!~*'().&=+$%-]+: )?[0-9a-z_!~*'().&=+$%-]+@)?" //user@ 
            + @"(([0-9]{1,3}\.){3}[0-9]{1,3}" // IP- 199.194.52.184 
            + "|" // allows either IP or domain 
            + @"([0-9a-z_!~*'()-]+\.)*" // tertiary domain(s)- www. 
            + @"([0-9a-z][0-9a-z-]{0,61})?[0-9a-z]" // second level domain 
            + @"(\.[a-z]{2,6})?)" // first level domain- .com or .museum 
            + "(:[0-9]{1,4})?" // port number- :80 
            + "((/?)|" // a slash isn't required if there is no file name 
            + "(/[0-9a-z_!~*'().;?:@&=+$,%#-]+)+/?)$", RegexOptions.IgnoreCase);

        public URLValidatorAttribute()
            : base(URLRegex)
        {
        }

        public override string FormatName
        {
            get { return "URL"; }
        }
    }

    public class FileNameValidatorAttribute : ValidatorAttribute
    {
        public static readonly char[] InvalidCharts = Path.GetInvalidPathChars();

        static readonly Regex invalidChartsRegex = new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]");

        public FileNameValidatorAttribute()
        {
        }

        public string FormatName
        {
            get { return ValidationMessage.FileName.NiceToString(); }
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.HaveValid0Format.NiceToString().FormatWith(FormatName); }
        }

        protected override string OverrideError(object value)
        {
            string str = (string)value;

            if (str == null)
                return null;

            if (str.IndexOfAny(InvalidCharts) == -1)
                return null;

            return ValidationMessage._0DoesNotHaveAValid1Format.NiceToString().FormatWith("{0}", FormatName);
        }

        public static string RemoveInvalidCharts(string a)
        {
            return invalidChartsRegex.Replace(a, "");
        }
    }

    public class DecimalsValidatorAttribute : ValidatorAttribute
    {
        public int DecimalPlaces { get; set; }

        public DecimalsValidatorAttribute()
        {
            DecimalPlaces = 2;
        }

        public DecimalsValidatorAttribute(int decimalPlaces)
        {
            this.DecimalPlaces = decimalPlaces;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            if (value is decimal && Math.Round((decimal)value, DecimalPlaces) != (decimal)value)
            {
                return ValidationMessage._0HasMoreThan1DecimalPlaces.NiceToString("{0}", DecimalPlaces);
            }

            return null;
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.Have0Decimals.NiceToString().FormatWith(DecimalPlaces); }
        }
    }


    public class NumberIsValidatorAttribute : ValidatorAttribute
    {
        public ComparisonType ComparisonType;
        public IComparable number;

        public NumberIsValidatorAttribute(ComparisonType comparison, float number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        public NumberIsValidatorAttribute(ComparisonType comparison, double number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        public NumberIsValidatorAttribute(ComparisonType comparison, byte number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        public NumberIsValidatorAttribute(ComparisonType comparison, short number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        public NumberIsValidatorAttribute(ComparisonType comparison, int number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        public NumberIsValidatorAttribute(ComparisonType comparison, long number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            IComparable val = (IComparable)value;

            if (number.GetType() != value.GetType())
                number = (IComparable)Convert.ChangeType(number, value.GetType()); // asi se hace solo una vez 

            bool ok = (ComparisonType == ComparisonType.EqualTo && val.CompareTo(number) == 0) ||
                      (ComparisonType == ComparisonType.DistinctTo && val.CompareTo(number) != 0) ||
                      (ComparisonType == ComparisonType.GreaterThan && val.CompareTo(number) > 0) ||
                      (ComparisonType == ComparisonType.GreaterThanOrEqualTo && val.CompareTo(number) >= 0) ||
                      (ComparisonType == ComparisonType.LessThan && val.CompareTo(number) < 0) ||
                      (ComparisonType == ComparisonType.LessThanOrEqualTo && val.CompareTo(number) <= 0);

            if (ok)
                return null;

            return ValidationMessage._0ShouldBe12.NiceToString().FormatWith("{0}", ComparisonType.NiceToString(), number.ToString());
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.Be.NiceToString() + ComparisonType.NiceToString() + " " + number.ToString(); }
        }
    }

    //Not using C intervals to please user!
    public class NumberBetweenValidatorAttribute : ValidatorAttribute
    {
        IComparable min;
        IComparable max;

        public NumberBetweenValidatorAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public NumberBetweenValidatorAttribute(double min, double max)
        {
            this.min = min;
            this.max = max;
        }

        public NumberBetweenValidatorAttribute(byte min, byte max)
        {
            this.min = min;
            this.max = max;
        }

        public NumberBetweenValidatorAttribute(short min, short max)
        {
            this.min = min;
            this.max = max;
        }

        public NumberBetweenValidatorAttribute(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        public NumberBetweenValidatorAttribute(long min, long max)
        {
            this.min = min;
            this.max = max;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            IComparable val = (IComparable)value;

            if (min.GetType() != value.GetType())
            {
                min = (IComparable)Convert.ChangeType(min, val.GetType()); // asi se hace solo una vez 
                max = (IComparable)Convert.ChangeType(max, val.GetType());
            }

            if (min.CompareTo(val) <= 0 &&
                val.CompareTo(max) <= 0)
                return null;

            return ValidationMessage._0HasToBeBetween1And2.NiceToString("{0}", min, max);
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.BeBetween0And1.NiceToString(min, max); }
        }
    }

    public class NoRepeatValidatorAttribute : ValidatorAttribute
    {
        protected override string OverrideError(object value)
        {
            IList list = (IList)value;
            if (list == null || list.Count <= 1)
                return null;
            string ex = list.Cast<object>().GroupCount().Where(kvp => kvp.Value > 1).ToString(e => "{0} x {1}".FormatWith(e.Key, e.Value), ", ");
            if (ex.HasText())
                return ValidationMessage._0HasSomeRepeatedElements1.NiceToString("{0}", ex);
            return null;
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.HaveNoRepeatedElements.NiceToString(); }
        }

        public static string ByKey<T, K>(IEnumerable<T> collection, Func<T, K> keySelector)
        {
            var errors = collection.GroupBy(keySelector)
                .Select(gr => new { gr.Key, Count = gr.Count() })
                .Where(a => a.Count > 1)
                .ToString(e => "{0} x {1}".FormatWith(e.Key, e.Count), ", ");

            return errors;
        }
    }

    public class CountIsValidatorAttribute : ValidatorAttribute
    {
        public ComparisonType ComparisonType;
        public int number;

        public CountIsValidatorAttribute(ComparisonType comparison, int number)
        {
            this.ComparisonType = comparison;
            this.number = number;
        }

        protected override string OverrideError(object value)
        {
            IList list = (IList)value;

            int val = list == null? 0: list.Count;

            if ((ComparisonType == ComparisonType.EqualTo && val.CompareTo(number) == 0) ||
                (ComparisonType == ComparisonType.DistinctTo && val.CompareTo(number) != 0) ||
                (ComparisonType == ComparisonType.GreaterThan && val.CompareTo(number) > 0) ||
                (ComparisonType == ComparisonType.GreaterThanOrEqualTo && val.CompareTo(number) >= 0) ||
                (ComparisonType == ComparisonType.LessThan && val.CompareTo(number) < 0) ||
                (ComparisonType == ComparisonType.LessThanOrEqualTo && val.CompareTo(number) <= 0))
                return null;

            return ValidationMessage.TheNumberOfElementsOf0HasToBe12.NiceToString().FormatWith("{0}", ComparisonType.NiceToString().FirstLower(), number.ToString());
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.HaveANumberOfElements01.NiceToString().FormatWith(ComparisonType.NiceToString().FirstLower(), number.ToString()); }
        }
    }

    public class DaysPrecissionValidatorAttribute : DateTimePrecissionValidatorAttribute
    {
        public DaysPrecissionValidatorAttribute()
            : base(DateTimePrecision.Days)
        { }
    }

    public class SecondsPrecissionValidatorAttribute : DateTimePrecissionValidatorAttribute
    {
        public SecondsPrecissionValidatorAttribute()
            : base(DateTimePrecision.Seconds)
        { }
    }

    public class MinutesPrecissionValidatorAttribute : DateTimePrecissionValidatorAttribute
    {
        public MinutesPrecissionValidatorAttribute()
            : base(DateTimePrecision.Minutes)
        { }

    }

    public class DateTimePrecissionValidatorAttribute : ValidatorAttribute
    {
        public DateTimePrecision Precision { get; private set; }

        public DateTimePrecissionValidatorAttribute(DateTimePrecision precision)
        {
            this.Precision = precision;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            var prec = ((DateTime)value).GetPrecision();
            if (prec > Precision)
                return ValidationMessage._0HasAPrecissionOf1InsteadOf2.NiceToString("{0}", prec, Precision);

            return null;
        }

        public string FormatString
        {
            get
            {
                var dtfi = CultureInfo.CurrentCulture.DateTimeFormat;
                switch (Precision)
                {
                    case DateTimePrecision.Days: return "d";
                    case DateTimePrecision.Hours: return dtfi.ShortDatePattern + " " + "HH";
                    case DateTimePrecision.Minutes: return "g";
                    case DateTimePrecision.Seconds: return "G";
                    case DateTimePrecision.Milliseconds: return dtfi.ShortDatePattern + " " + dtfi.LongTimePattern.Replace("ss", "ss.fff");
                    default: return "";
                }
            }
        }

        public override string HelpMessage
        {
            get
            {
                return ValidationMessage.HaveAPrecisionOf.NiceToString() + " " + Precision.NiceToString().ToLower();
            }
        }
    }

    public class DateInPastValidator : ValidatorAttribute
    {
        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            if (((DateTime)value) > TimeZoneManager.Now)
                return ValidationMessage._0ShouldBeADateInThePast.NiceToString();

            return null;
        }

        public override string HelpMessage
        {
            get
            {
                return ValidationMessage.BeInThePast.NiceToString();
            }
        }
    }

    public class TimeSpanPrecissionValidatorAttribute : ValidatorAttribute
    {
        public DateTimePrecision Precision { get; private set; }

        public TimeSpanPrecissionValidatorAttribute(DateTimePrecision precision)
        {
            this.Precision = precision;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            var prec = ((TimeSpan)value).GetPrecision();
            if (prec > Precision)
                return "{0} has a precission of {1} instead of {2}".FormatWith("{0}", prec, Precision);

            if(((TimeSpan)value).Days != 0)
                return "{0} has days";

            return null;
        }

        public string FormatString
        {
            get
            {
                switch (Precision)
                {
                    case DateTimePrecision.Hours: return "hh";
                    case DateTimePrecision.Minutes: return @"hh\:mm";
                    case DateTimePrecision.Seconds: return @"hh\:mm\:ss";
                    case DateTimePrecision.Milliseconds: return "c";
                    default: return "";
                }
            }
        }

        public override string HelpMessage
        {
            get
            {
                return ValidationMessage.HaveAPrecisionOf.NiceToString() + " " + Precision.NiceToString().ToLower();
            }
        }
    }

    public class StringCaseValidatorAttribute : ValidatorAttribute
    {
        private Case textCase;
        public Case TextCase
        {
            get { return this.textCase; }
            set { this.textCase = value; }
        }

        public StringCaseValidatorAttribute(Case textCase)
        {
            this.textCase = textCase;
        }

        protected override string OverrideError(object value)
        {
            if (string.IsNullOrEmpty((string)value)) return null;

            string str = (string)value;

            if ((this.textCase == Case.Uppercase) && (str != str.ToUpper()))
                return ValidationMessage._0HasToBeUppercase.NiceToString();

            if ((this.textCase == Case.Lowercase) && (str != str.ToLower()))
                return ValidationMessage._0HasToBeLowercase.NiceToString();

            return null;
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.Be.NiceToString() + textCase.NiceToString(); }
        }
    }

    [DescriptionOptions(DescriptionOptions.Members)]
    public enum Case
    {
        Uppercase,
        Lowercase
    }
    
    [DescriptionOptions(DescriptionOptions.Members)]
    public enum ComparisonType
    {
        EqualTo,
        DistinctTo,
        GreaterThan,
        GreaterThanOrEqualTo,
        LessThan,
        LessThanOrEqualTo,
    }

    public class IsAssignableToValidatorAttribute : ValidatorAttribute
    {
        public Type Type { get; set; }

        public IsAssignableToValidatorAttribute(Type type)
        {
            this.Type = type;
        }

        protected override string OverrideError(object value)
        {
            if (value == null)
                return null;

            var t = (TypeEntity)value;
            if (!Type.IsAssignableFrom(t.ToType()))
            {
                return ValidationMessage._0IsNotA1_G.NiceToString().ForGenderAndNumber(Type.GetGender()).FormatWith(t.ToType().NiceName(), Type.NiceName());
            }

            return null;
        }

        public override string HelpMessage
        {
            get { return ValidationMessage.BeA0_G.NiceToString().ForGenderAndNumber(Type.GetGender()).FormatWith(Type.NiceName()); }
        }
    }

    public class StateValidator<E, S> : IEnumerable
        where E : ModifiableEntity
        where S : struct
    {
        Func<E, S> getState;
        string[] propertyNames;
        PropertyInfo[] properties;
        Func<E, object>[] getters;

        Dictionary<S, bool?[]> dictionary = new Dictionary<S, bool?[]>();

        public StateValidator(Func<E, S> getState, params Expression<Func<E, object>>[] propertyGetters)
        {
            this.getState = getState;
            this.properties = propertyGetters.Select(p => ReflectionTools.GetPropertyInfo(p)).ToArray();
            this.propertyNames = this.properties.Select(pi => pi.Name).ToArray();
            this.getters = propertyGetters.Select(p => p.Compile()).ToArray();
        }

        public void Add(S state, params bool?[] necessary)
        {
            if (necessary != null && necessary.Length != propertyNames.Length)
                throw new ArgumentException("The StateValidator {0} for state {1} has {2} values instead of {3}"
                    .FormatWith(GetType().TypeName(), state, necessary.Length, propertyNames.Length));

            dictionary.Add(state, necessary);
        }

        public string Validate(E entity, PropertyInfo pi)
        {
            return Validate(entity, pi, true);
        }

        public bool? IsAllowed(S state, PropertyInfo pi)
        {
            int index = propertyNames.IndexOf(pi.Name);
            if (index == -1)
                return null;

            return Necessary(state, index);
        }

      

        public string Validate(E entity, PropertyInfo pi, bool showState)
        {
            int index = propertyNames.IndexOf(pi.Name);
            if (index == -1)
                return null;

            S state = getState(entity);

            return GetMessage(entity, state, showState, index);
        }

        private string GetMessage(E entity, S state, bool showState, int index)
        {
            bool? necessary = Necessary(state, index);

            if (necessary == null)
                return null;

            object val = getters[index](entity);
            if (val is IList && ((IList)val).Count == 0 || val is string && ((string)val).Length == 0) //both are indistinguible after retrieving
                val = null;

            if (val != null && !necessary.Value)
                return showState ? ValidationMessage._0IsNotAllowedOnState1.NiceToString().FormatWith(properties[index].NiceName(), state) :
                                   ValidationMessage._0IsNotAllowed.NiceToString().FormatWith(properties[index].NiceName());

            if (val == null && necessary.Value)
                return showState ? ValidationMessage._0IsNecessaryOnState1.NiceToString().FormatWith(properties[index].NiceName(), state) :
                                   ValidationMessage._0IsNecessary.NiceToString().FormatWith(properties[index].NiceName());

            return null;
        }

        public bool? Necessary(S state, PropertyInfo pi)
        {
            int index = propertyNames.IndexOf(pi.Name);
            if (index == -1)
                throw new ArgumentException("The property is not registered");

            return Necessary(state, index);
        }

        bool? Necessary(S state, int index)
        {
            return dictionary.GetOrThrow(state, "State {0} not registered in StateValidator")[index];
        }

        public IEnumerator GetEnumerator() //just to use object initializer
        {
            throw new NotImplementedException();
        }

        public string PreviewErrors(E entity, S targetState, bool showState)
        {
            string result = propertyNames.Select((pn, i) => GetMessage(entity, targetState, showState, i)).NotNull().ToString("\r\n");

            return string.IsNullOrEmpty(result) ? null : result;
        }
    }


    public enum ValidationMessage
    {
        [Description("{0} does not have a valid {0} format")]
        _0DoesNotHaveAValid1Format,
        [Description("{0} has an invalid format")]
        _0HasAnInvalidFormat,
        [Description("{0} has more than {1} decimal places")]
        _0HasMoreThan1DecimalPlaces,
        [Description("{0} has some repeated elements: {1}")]
        _0HasSomeRepeatedElements1,
        [Description("{0} should be {1} {2}")]
        _0ShouldBe12,
        [Description("{0} has to be between {1} and {2}")]
        _0HasToBeBetween1And2,
        [Description("{0} has to be lowercase")]
        _0HasToBeLowercase,
        [Description("{0} has to be uppercase")]
        _0HasToBeUppercase,
        [Description("{0} is necessary")]
        _0IsNecessary,
        [Description("{0} is necessary on state {1}")]
        _0IsNecessaryOnState1,
        [Description("{0} is not allowed")]
        _0IsNotAllowed,
        [Description("{0} is not allowed on state {1}")]
        _0IsNotAllowedOnState1,
        [Description("{0} is not set")]
        _0IsNotSet,
        [Description("{0} is set")]
        _0IsSet,
        [Description("{0} is not a {1}")]
        _0IsNotA1_G,
        [Description("be a {0}")]
        BeA0_G,
        [Description("be ")]
        Be,
        [Description("be between {0} and {1}")]
        BeBetween0And1,
        [Description("be not null")]
        BeNotNull,
        [Description("file name")]
        FileName,
        [Description("have {0} decimals")]
        Have0Decimals,
        [Description("have a number of elements {0} {1}")]
        HaveANumberOfElements01,
        [Description("have a precision of ")]
        HaveAPrecisionOf,
        [Description("have between {0} and {1} characters")]
        HaveBetween0And1Characters,
        [Description("have maximum {0} characters")]
        HaveMaximum0Characters,
        [Description("have minimum {0} characters")]
        HaveMinimum0Characters,
        [Description("have no repeated elements")]
        HaveNoRepeatedElements,
        [Description("have a valid {0} format")]
        HaveValid0Format,
        InvalidDateFormat,
        InvalidFormat,
        [Description("Not possible to assign {0}")]
        NotPossibleToaAssign0,
        Numeric,
        [Description("or be null")]
        OrBeNull,
        Telephone,
        [Description("{0} should not have break lines")]
        _0ShouldNotHaveBreakLines,
        [Description("{0} should not have initial spaces")]
        _0ShouldNotHaveInitialSpaces,
        [Description("{0} should not have final spaces")]
        _0ShouldNotHaveFinalSpaces,
        [Description("The lenght of {0} has to be equal to {1}")]
        TheLenghtOf0HasToBeEqualTo1,
        [Description("The length of {0} has to be greater than or equal to {1}")]
        TheLengthOf0HasToBeGreaterOrEqualTo1,
        [Description("The length of {0} has to be less than or equal to {1}")]
        TheLengthOf0HasToBeLesserOrEqualTo1,
        [Description("The number of {0} is being multiplied by {1}")]
        TheNumberOf0IsBeingMultipliedBy1,
        [Description("The number of elements of {0} has to be {0} {1}")]
        TheNumberOfElementsOf0HasToBe12,
        [Description("Type {0} not allowed")]
        Type0NotAllowed,

        [Description("{0} is mandatory when {1} is not set")]
        _0IsMandatoryWhen1IsNotSet,
        [Description("{0} is mandatory when {1} is set")]
        _0IsMandatoryWhen1IsSet,
        [Description("{0} should be null when {1} is not set")]
        _0ShouldBeNullWhen1IsNotSet,
        [Description("{0} should be null when {1} is set")]
        _0ShouldBeNullWhen1IsSet,
        [Description("{0} should be null")]
        _0ShouldBeNull,
        [Description("{0} should be a date in the past")]
        _0ShouldBeADateInThePast,
        BeInThePast,
        [Description("{0} should be greater than {1}")]
        _0ShouldBeGreaterThan1,
        [Description("{0} has a precission of {1} instead of {2}")]
        _0HasAPrecissionOf1InsteadOf2,
    }
}