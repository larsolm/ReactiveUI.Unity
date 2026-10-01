namespace ExCSS
{
    /// <summary>
    /// How a media feature's value is compared, covering both the <c>min-</c>/<c>max-</c> prefixes and
    /// the Media Queries 4 range syntax.
    /// </summary>
    /// <remarks>
    /// A public mirror of the constraint delimiter token, which cannot be surfaced directly because
    /// <c>TokenType</c> is internal. Without it a consumer cannot tell <c>(width &gt; 40rem)</c> from
    /// <c>(width: 40rem)</c>, since the prefix flags describe only the name.
    /// </remarks>
    public enum MediaFeatureComparison : byte
    {
        None,
        Equal,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual
    }
}
