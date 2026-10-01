using System;
using System.IO;

namespace ExCSS
{
    public abstract class MediaFeature : StylesheetNode, IMediaFeature
    {
        private TokenValue _tokenValue;

        internal MediaFeature(string name)
        {
            Name = name;
            IsMinimum = name.StartsWith("min-");
            IsMaximum = name.StartsWith("max-");
        }

        internal abstract IValueConverter Converter { get; }

        public bool IsMinimum { get; }

        public bool IsMaximum { get; }

        public string Name { get; }

        public string Value => HasValue ? _tokenValue.Text : string.Empty;

        public bool HasValue => _tokenValue is {Count: > 0};

        /// <summary>
        /// How this feature's value is compared. <see cref="MediaFeatureComparison.Equal"/> for the
        /// colon form, including the <c>min-</c>/<c>max-</c> prefixes, whose direction lives in the
        /// name rather than the delimiter.
        /// </summary>
        public MediaFeatureComparison Comparison { get; private set; } = MediaFeatureComparison.Equal;

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var constraintDelimiter = GetConstraintDelimiter();
            var value = HasValue ? Value : null;
            writer.Write(formatter.Constraint(Name, value, GetConstraintDelimiter()));
        }

        private string GetConstraintDelimiter()
        {
            switch (Comparison)
            {
                case MediaFeatureComparison.GreaterThan: return " > ";
                case MediaFeatureComparison.LessThan: return " < ";
                case MediaFeatureComparison.GreaterThanOrEqual: return " >= ";
                case MediaFeatureComparison.LessThanOrEqual: return " <= ";
                default: return ": ";
            }
        }

        private static MediaFeatureComparison ToComparison(TokenType constraintDelimiter)
        {
            switch (constraintDelimiter)
            {
                case TokenType.GreaterThan: return MediaFeatureComparison.GreaterThan;
                case TokenType.LessThan: return MediaFeatureComparison.LessThan;
                case TokenType.GreaterThanOrEqual: return MediaFeatureComparison.GreaterThanOrEqual;
                case TokenType.LessThanOrEqual: return MediaFeatureComparison.LessThanOrEqual;
                default: return MediaFeatureComparison.Equal;
            }
        }

        internal bool TrySetValue(TokenValue tokenValue, TokenType constraintDelimiter)
        {
            bool result;

            if (tokenValue == null)
                result = !IsMinimum && !IsMaximum && Converter.ConvertDefault() != null;
            else
                result = Converter.Convert(tokenValue) != null;

            if (result) _tokenValue = tokenValue;

            Comparison = ToComparison(constraintDelimiter);

            return result;
        }
    }
}