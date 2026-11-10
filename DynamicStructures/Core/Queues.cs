namespace DynamicStructures.Core;

public interface IQueue<T>
{
    void Enqueue(T value);
    T Dequeue();
    T Peek();
    bool IsEmpty();
    string Print();
}

/// <summary>Очередь на List&lt;T&gt;: удаление из начала сдвигает все элементы — O(n).</summary>
public sealed class ListQueue<T> : IQueue<T>
{
    private readonly List<T> _items = [];

    public void Enqueue(T value) => _items.Add(value);

    public T Dequeue()
    {
        var value = Peek();
        _items.RemoveAt(0);
        return value;
    }

    public T Peek() => _items.Count > 0 ? _items[0] : throw new InvalidOperationException("Очередь пуста.");
    public bool IsEmpty() => _items.Count == 0;
    public string Print() => _items.Count == 0 ? "(пусто)" : string.Join(" ", _items);
}

/// <summary>Очередь на собственном односвязном списке: вставка в хвост и удаление из головы — O(1).</summary>
public sealed class LinkedQueue<T> : IQueue<T> where T : notnull
{
    private readonly SinglyLinkedList<T> _items = new();

    public void Enqueue(T value) => _items.AddLast(value);
    public T Dequeue() => _items.IsEmpty ? throw new InvalidOperationException("Очередь пуста.") : _items.RemoveFirst();
    public T Peek() => _items.Head is { } head ? head.Value : throw new InvalidOperationException("Очередь пуста.");
    public bool IsEmpty() => _items.IsEmpty;
    public string Print() => _items.ToString();
}

/// <summary>Обёртка над стандартным Queue&lt;T&gt;.</summary>
public sealed class StandardQueue<T> : IQueue<T>
{
    private readonly Queue<T> _items = new();

    public void Enqueue(T value) => _items.Enqueue(value);
    public T Dequeue() => _items.Count > 0 ? _items.Dequeue() : throw new InvalidOperationException("Очередь пуста.");
    public T Peek() => _items.Count > 0 ? _items.Peek() : throw new InvalidOperationException("Очередь пуста.");
    public bool IsEmpty() => _items.Count == 0;
    public string Print() => _items.Count == 0 ? "(пусто)" : string.Join(" ", _items);
}
