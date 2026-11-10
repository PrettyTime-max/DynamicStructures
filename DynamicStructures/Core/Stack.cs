namespace DynamicStructures.Core;

/// <summary>Стек на собственном связном списке: вершина — голова списка.</summary>
public sealed class LinkedStack<T> where T : notnull
{
    private readonly SinglyLinkedList<T> _items = new();

    public int Count => _items.Count;
    public IEnumerable<T> Items => _items;
    public bool IsEmpty() => _items.IsEmpty;
    public void Push(T value) => _items.AddFirst(value);

    public T Pop() => IsEmpty() ? throw new InvalidOperationException("Стек пуст.") : _items.RemoveFirst();

    public T Top() => _items.Head is { } head ? head.Value : throw new InvalidOperationException("Стек пуст.");

    public string Print() => _items.ToString();
}
