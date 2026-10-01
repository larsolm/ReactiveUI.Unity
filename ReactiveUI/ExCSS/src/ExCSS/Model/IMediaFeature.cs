namespace ExCSS
{
    public interface IMediaFeature : IStylesheetNode
    {
        string Name { get; }
        string Value { get; }
        bool HasValue { get; }

        /// <summary>How this feature's value is compared, including the range syntax.</summary>
        MediaFeatureComparison Comparison { get; }
    }
}
