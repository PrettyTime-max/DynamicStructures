using System.Collections;

namespace DynamicStructures.Core;

public sealed class Node<T>(T value)
{
    public T Value { get; set; } = value;
    public Node<T>? Next { get; set; }
}

/// <summary>Собственный односвязный список: основа для стека и алгоритмов части 4.</summary>
public sealed class SinglyLinkedList<T> : IEnumerable<T> where T : notnull
{
    public Node<T>? Head { get; private set; }
    public Node<T>? Tail { get; private set; }
    public int Count { get; private set; }
    public bool IsEmpty => Head is null;

    public SinglyLinkedList() { }

    public SinglyLinkedList(IEnumerable<T> items)
    {
        foreach (var item in items) AddLast(item);
    }

    public void AddFirst(T value)
    {
        var node = new Node<T>(value) { Next = Head };
        Head = node;
        Tail ??= node;
        Count++;
    }

    public void AddLast(T value)
    {
        var node = new Node<T>(value);
        if (Tail is null) Head = node;
        else Tail.Next = node;
        Tail = node;
        Count++;
    }

    public T RemoveFirst()
    {
        var head = Head ?? throw new InvalidOperationException("Список пуст.");
        Head = head.Next;
        if (Head is null) Tail = null;
        Count--;
        return head.Value;
    }

    /// <summary>1. Переворачивает список, меняя ссылки узлов.</summary>
    public void Reverse()
    {
        Node<T>? prev = null, current = Head;
        Tail = Head;
        while (current is not null)
        {
            var next = current.Next;
            current.Next = prev;
            prev = current;
            current = next;
        }
        Head = prev;
    }

    /// <summary>2а. Переносит последний элемент в начало списка.</summary>
    public void MoveLastToFront()
    {
        if (Head is null) throw new InvalidOperationException("Список пуст.");
        if (Head == Tail) return;
        var beforeTail = Head;
        while (beforeTail.Next != Tail) beforeTail = beforeTail.Next!;
        beforeTail.Next = null;
        Tail!.Next = Head;
        Head = Tail;
        Tail = beforeTail;
    }

    /// <summary>2б. Переносит первый элемент в конец списка.</summary>
    public void MoveFirstToBack()
    {
        if (Head is null) throw new InvalidOperationException("Список пуст.");
        if (Head == Tail) return;
        var first = Head;
        Head = first.Next;
        first.Next = null;
        Tail!.Next = first;
        Tail = first;
    }

    /// <summary>3. Количество различных элементов.</summary>
    public int CountDistinct()
    {
        var seen = new HashSet<T>();
        for (var node = Head; node is not null; node = node.Next) seen.Add(node.Value);
        return seen.Count;
    }

    /// <summary>4. Удаляет все элементы, которые встречаются больше одного раза.</summary>
    public void RemoveNonUnique()
    {
        var counts = new Dictionary<T, int>();
        for (var node = Head; node is not null; node = node.Next)
            counts[node.Value] = counts.GetValueOrDefault(node.Value) + 1;

        Node<T>? prev = null;
        for (var node = Head; node is not null; node = node.Next)
        {
            if (counts[node.Value] > 1)
            {
                if (prev is null) Head = node.Next;
                else prev.Next = node.Next;
                Count--;
            }
            else prev = node;
        }
        Tail = prev;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var node = Head; node is not null; node = node.Next) yield return node.Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => IsEmpty ? "(пусто)" : string.Join(" ", this);
}
