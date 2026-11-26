namespace NetworkingDemo.Networking;

ref struct SpanSplitter
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly byte _delimiter;
    
    public SpanSplitter(ReadOnlySpan<byte> data, byte delimiter)
    {
        _data = data;
        _delimiter = delimiter;
    }
    
    public SpanSplitEnumerator GetEnumerator()
    {
        return new SpanSplitEnumerator(_data, _delimiter);
    }
}
