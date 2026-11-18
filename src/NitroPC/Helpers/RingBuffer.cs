namespace NitroPC.Helpers;

internal sealed class RingBuffer
{
    private readonly double[] _values;
    private int _next;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _values = new double[capacity];
    }

    public int Count => _count;

    public void Push(double value)
    {
        if (_count == 0)
        {
            Array.Fill(_values, value);
            _count = _values.Length;
        }
        else
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
        }
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        for (var index = 0; index < _count; index++)
        {
            result[index] = _values[(_next + index) % _values.Length];
        }

        return result;
    }
}
