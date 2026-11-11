using System.Collections;

namespace LibXbf.Records;

/// <summary>
/// One of the metadata tables at the start of an XBF file. Entries are referenced by index from other tables
/// and from the node stream.
/// </summary>
public sealed class XbfTable<T> : IReadOnlyList<T>
{
    private readonly T[] _values;

    internal XbfTable(string name, BinaryReader br, int minimumEntrySize, Func<BinaryReader, T> readEntry)
    {
        Name = name;
        int count = br.ReadCount(minimumEntrySize);

        _values = new T[count];
        for (int i = 0; i < count; i++)
        {
            _values[i] = readEntry(br);
        }
    }

    public string Name { get; }

    public int Count => _values.Length;

    public IReadOnlyList<T> Values => _values;

    /// <exception cref="InvalidXbfException">The index is outside the table.</exception>
    public T this[uint index] => index < (uint)_values.Length
        ? _values[index]
        : throw new InvalidXbfException($"Reference to entry {index} of the {Name} table, which only has {_values.Length} entries.");

    T IReadOnlyList<T>.this[int index] => this[(uint)index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
