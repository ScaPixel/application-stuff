namespace NetworkingDemo.Networking;

ref struct SpanSplitEnumerator
{
    private ReadOnlySpan<byte> _remaining;
    private readonly byte _delimiter;

    public ReadOnlySpan<byte> Current { get; private set; }

    public SpanSplitEnumerator(ReadOnlySpan<byte> data, byte delimiter)
    {
        _remaining = data;
        _delimiter = delimiter;
        Current = default;
    }

    public bool MoveNext()
    {
        if (_remaining.IsEmpty) return false;

        int index = _remaining.IndexOf(_delimiter);

        if (index < 0)
        {
            Current = _remaining;
            _remaining = ReadOnlySpan<byte>.Empty;
            return true;
        }
        
        Current = _remaining[..index];
        _remaining = _remaining[(index + 1)..];
        return true;
    }
}
