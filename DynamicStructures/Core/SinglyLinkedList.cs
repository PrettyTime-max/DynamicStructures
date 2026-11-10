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

    /// <summary>5. Вставляет копию списка в него самого вслед за первым вхождением x.</summary>
    public bool InsertSelfAfter(T x)
    {
        var target = Find(x);
        if (target is null) return false;
        var copy = new SinglyLinkedList<T>(this);
        copy.Tail!.Next = target.Next;
        target.Next = copy.Head;
        if (target == Tail) Tail = copy.Tail;
        Count += copy.Count;
        return true;
    }

    public bool IsSorted()
    {
        for (var node = Head; node?.Next is not null; node = node.Next)
            if (Comparer<T>.Default.Compare(node.Value, node.Next.Value) > 0) return false;
        return true;
    }

    /// <summary>6. Вставляет элемент в упорядоченный по неубыванию список, сохраняя порядок.</summary>
    public void InsertSorted(T value)
    {
        var comparer = Comparer<T>.Default;
        if (Head is null || comparer.Compare(value, Head.Value) < 0)
        {
            AddFirst(value);
            return;
        }
        var node = Head;
        while (node.Next is not null && comparer.Compare(node.Next.Value, value) <= 0) node = node.Next;
        node.Next = new Node<T>(value) { Next = node.Next };
        if (node == Tail) Tail = node.Next;
        Count++;
    }

    /// <summary>7. Удаляет все элементы, равные value; возвращает число удалённых.</summary>
    public int RemoveAll(T value)
    {
        var removed = 0;
        Node<T>? prev = null;
        for (var node = Head; node is not null; node = node.Next)
        {
            if (Same(node.Value, value))
            {
                if (prev is null) Head = node.Next;
                else prev.Next = node.Next;
                removed++;
            }
            else prev = node;
        }
        Tail = prev;
        Count -= removed;
        return removed;
    }

    /// <summary>8. Вставляет value перед первым вхождением target, если оно есть.</summary>
    public bool InsertBefore(T target, T value)
    {
        if (Head is null) return false;
        if (Same(Head.Value, target))
        {
            AddFirst(value);
            return true;
        }
        for (var node = Head; node.Next is not null; node = node.Next)
        {
            if (!Same(node.Next.Value, target)) continue;
            node.Next = new Node<T>(value) { Next = node.Next };
            Count++;
            return true;
        }
        return false;
    }

    /// <summary>9 и 11. Дописывает в конец элементы другого списка (или самого себя — удвоение).</summary>
    public void Append(SinglyLinkedList<T> other)
    {
        var node = other.Head;
        for (var left = other.Count; left > 0; left--, node = node.Next) AddLast(node!.Value);
    }

    /// <summary>10. Отрезает хвост, начиная с первого вхождения value, и возвращает его отдельным списком.</summary>
    public SinglyLinkedList<T> SplitAt(T value)
    {
        var second = new SinglyLinkedList<T>();
        Node<T>? prev = null;
        var node = Head;
        var index = 0;
        while (node is not null && !Same(node.Value, value))
        {
            prev = node;
            node = node.Next;
            index++;
        }
        if (node is null) return second;

        second.Head = node;
        second.Tail = Tail;
        second.Count = Count - index;
        if (prev is null) Head = null;
        else prev.Next = null;
        Tail = prev;
        Count = index;
        return second;
    }

    /// <summary>12. Меняет местами два узла по их индексам (с нуля), переставляя ссылки.</summary>
    public void Swap(int first, int second)
    {
        if (first < 0 || second < 0 || first >= Count || second >= Count)
            throw new ArgumentOutOfRangeException(null, $"Позиции должны быть от 1 до {Count}.");
        if (first == second) return;
        if (first > second) (first, second) = (second, first);

        Node<T>? prevA = null;
        var a = Head!;
        for (var i = 0; i < first; i++) { prevA = a; a = a.Next!; }
        var prevB = a;
        var b = a.Next!;
        for (var i = first + 1; i < second; i++) { prevB = b; b = b.Next!; }

        if (prevA is null) Head = b;
        else prevA.Next = b;
        var afterB = b.Next;
        if (prevB == a) b.Next = a;
        else
        {
            b.Next = a.Next;
            prevB.Next = a;
        }
        a.Next = afterB;
        if (afterB is null) Tail = a;
    }

    /// <summary>12. Меняет местами узлы с первыми вхождениями a и b; false, если одного из значений нет.</summary>
    public bool SwapValues(T a, T b)
    {
        int first = IndexOf(a), second = IndexOf(b);
        if (first < 0 || second < 0) return false;
        Swap(first, second);
        return true;
    }

    private int IndexOf(T value)
    {
        var index = 0;
        for (var node = Head; node is not null; node = node.Next, index++)
            if (Same(node.Value, value)) return index;
        return -1;
    }

    private Node<T>? Find(T value)
    {
        for (var node = Head; node is not null; node = node.Next)
            if (Same(node.Value, value)) return node;
        return null;
    }

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

    public IEnumerator<T> GetEnumerator()
    {
        for (var node = Head; node is not null; node = node.Next) yield return node.Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => IsEmpty ? "(пусто)" : string.Join(" ", this);
}
