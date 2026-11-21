namespace DynamicStructures.Core;

/// <summary>Узел на кадре: <paramref name="Next"/> — идентификатор узла, на который указывает ссылка (null — ссылка пуста).</summary>
public sealed record FrameNode(int Id, string Value, int? Next, int Row, int Col);

/// <summary>Подпись указателя (head, cur, prev…); <paramref name="Node"/> = null — указатель равен null.</summary>
public sealed record FrameMark(string Name, int? Node);

public sealed record ListFrame(string Note, IReadOnlyList<FrameNode> Nodes, IReadOnlyList<FrameMark> Marks, IReadOnlySet<int> Hot, IReadOnlySet<int> Doomed);

public sealed record ListAnimationResult(IReadOnlyList<ListFrame> Frames, string? Skipped);

internal sealed class TNode(int id, int value, int row)
{
    public int Id { get; } = id;
    public int Value { get; } = value;
    public int Row { get; set; } = row;
    public TNode? Next { get; set; }
}

/// <summary>Записывает кадры работы алгоритма: узлы, ссылки и указатели после каждого шага.</summary>
internal sealed class ListTrace
{
    private int _nextId;
    private readonly List<TNode> _alive = [];
    private readonly List<(string Name, TNode? Node)> _marks = [];

    public TNode? Head { get; set; }
    public TNode? Tail { get; set; }
    public TNode? Head2 { get; set; }
    public string HeadLabel { get; set; } = "head";
    public string Head2Label { get; set; } = "head₂";
    public HashSet<TNode> Hot { get; } = [];
    public HashSet<TNode> Doomed { get; } = [];
    public List<ListFrame> Frames { get; } = [];

    public static ListTrace From(IEnumerable<int> values)
    {
        var trace = new ListTrace();
        foreach (var value in values)
        {
            var node = trace.Add(value);
            if (trace.Tail is null) trace.Head = node;
            else trace.Tail.Next = node;
            trace.Tail = node;
        }
        return trace;
    }

    public TNode Add(int value, int row = 0)
    {
        var node = new TNode(++_nextId, value, row);
        _alive.Add(node);
        return node;
    }

    public void BuildSecond(IEnumerable<int> values)
    {
        TNode? tail = null;
        foreach (var value in values)
        {
            var node = Add(value, 1);
            if (tail is null) Head2 = node;
            else tail.Next = node;
            tail = node;
        }
    }

    public void Remove(TNode node) => _alive.Remove(node);

    public void Mark(string name, TNode? node)
    {
        var index = _marks.FindIndex(m => m.Name == name);
        if (index >= 0) _marks[index] = (name, node);
        else _marks.Add((name, node));
    }

    public void Unmark(string name) => _marks.RemoveAll(m => m.Name == name);

    public List<TNode> Chain()
    {
        var chain = new List<TNode>();
        for (var node = Head; node is not null && !chain.Contains(node); node = node.Next) chain.Add(node);
        return chain;
    }

    /// <summary>Выстраивает узлы на экране в порядке ссылок: первый список в верхней строке, второй — в нижней.</summary>
    public void Relayout()
    {
        var order = new List<TNode>();
        void Walk(TNode? start, int row)
        {
            for (var node = start; node is not null && !order.Contains(node); node = node.Next)
            {
                node.Row = row;
                order.Add(node);
            }
        }
        Walk(Head, 0);
        Walk(Head2, 1);
        var rest = _alive.Where(n => !order.Contains(n)).ToList();
        _alive.Clear();
        _alive.AddRange(order);
        _alive.AddRange(rest);
    }

    public void Snap(string note)
    {
        var columns = new Dictionary<int, int>();
        var nodes = _alive.Select(n =>
        {
            columns.TryGetValue(n.Row, out var column);
            columns[n.Row] = column + 1;
            return new FrameNode(n.Id, n.Value.ToString(), n.Next?.Id, n.Row, column);
        }).ToList();

        var marks = new List<FrameMark> { new(HeadLabel, Head?.Id), new("tail", Tail?.Id) };
        if (Head2 is not null) marks.Add(new(Head2Label, Head2.Id));
        marks.AddRange(_marks.Where(m => m.Node is null || _alive.Contains(m.Node)).Select(m => new FrameMark(m.Name, m.Node?.Id)));

        Frames.Add(new ListFrame(note, nodes, marks,
            Hot.Where(_alive.Contains).Select(n => n.Id).ToHashSet(),
            Doomed.Where(_alive.Contains).Select(n => n.Id).ToHashSet()));
        Hot.Clear();
        Doomed.Clear();
    }
}

/// <summary>Пошаговые кадры для двенадцати алгоритмов части 4. Номер алгоритма совпадает с номером в <see cref="ListAlgorithms.All"/>.</summary>
public static class ListAnimation
{
    public const int MaxNodes = 30;

    public static ListAnimationResult Build(int number, IReadOnlyList<int> list, IReadOnlyList<int>? second, int[] args)
    {
        var frames = number switch
        {
            1 => Reverse(list),
            2 => MoveEnds(list),
            3 => Distinct(list),
            4 => NonUnique(list),
            5 => InsertSelf(list, args[0]),
            6 => InsertSorted(list, args[0]),
            7 => RemoveAll(list, args[0]),
            8 => InsertBefore(list, args[0], args[1]),
            9 => AppendList(list, second ?? []),
            10 => Split(list, args[0]),
            11 => Double(list),
            12 => Swap(list, args[0], args[1]),
            _ => [],
        };
        var most = frames.Count == 0 ? 0 : frames.Max(f => f.Nodes.Count);
        return most > MaxNodes
            ? new([], $"Анимация не показана: на экране одновременно оказывается {most} узлов, а помещается не больше {MaxNodes}. Результат алгоритма выведен выше.")
            : new(frames, null);
    }

    private static List<ListFrame> Reverse(IReadOnlyList<int> list)
    {
        var t = ListTrace.From(list);
        t.Snap("Исходный список. Будем разворачивать ссылку Next у каждого узла.");
        TNode? prev = null, cur = t.Head;
        t.Tail = t.Head;
        t.Mark("prev", prev);
        t.Mark("cur", cur);
        t.Snap("prev = null, cur = head. Бывшая голова станет хвостом: tail = head.");
        while (cur is not null)
        {
            var next = cur.Next;
            cur.Next = prev;
            t.Hot.Add(cur);
            t.Mark("next", next);
            t.Snap($"Запоминаем next = cur.Next и разворачиваем ссылку: cur.Next = prev. У узла {cur.Value} стрелка теперь смотрит назад.");
            prev = cur;
            cur = next;
            t.Mark("prev", prev);
            t.Mark("cur", cur);
            t.Unmark("next");
            t.Snap("Сдвигаем prev и cur на шаг вперёд.");
        }
        t.Head = prev;
        t.Snap("Список закончился. head = prev: головой стал бывший последний узел.");
        t.Relayout();
        t.Snap("Узлы выстроены в порядке новых ссылок. Список перевёрнут.");
        return t.Frames;
    }

    private static List<ListFrame> MoveEnds(IReadOnlyList<int> list)
    {
        var frames = new List<ListFrame>();

        var a = ListTrace.From(list);
        a.Snap("Часть а. Переносим последний элемент в начало списка.");
        if (a.Head != a.Tail)
        {
            var before = a.Head!;
            a.Mark("cur", before);
            a.Snap("Ищем предпоследний узел: идём от головы, пока cur.Next не станет хвостом. Односвязный список назад ходить не умеет.");
            while (before.Next != a.Tail)
            {
                before = before.Next!;
                a.Mark("cur", before);
                a.Snap("cur.Next ещё не хвост — идём дальше.");
            }
            a.Hot.Add(before);
            a.Snap("Предпоследний узел найден.");
            before.Next = null;
            a.Snap("cur.Next = null — отрезаем хвост от списка.");
            a.Tail!.Next = a.Head;
            a.Snap("tail.Next = head — бывший хвост ссылается на бывшую голову.");
            a.Head = a.Tail;
            a.Snap("head = tail — бывший хвост стал головой.");
            a.Tail = before;
            a.Snap("tail = cur — новым хвостом стал предпоследний узел.");
            a.Relayout();
            a.Snap("Узлы выстроены по ссылкам: последний элемент перенесён в начало.");
        }
        else a.Snap("В списке один узел — переносить нечего.");
        frames.AddRange(a.Frames);

        var b = ListTrace.From(list);
        b.Snap("Часть б. Снова исходный список. Переносим первый элемент в конец.");
        if (b.Head != b.Tail)
        {
            var first = b.Head!;
            b.Mark("first", first);
            b.Snap("first = head — запоминаем первый узел.");
            b.Head = first.Next;
            b.Snap("head = first.Next — голова сдвинулась на второй узел.");
            first.Next = null;
            b.Snap("first.Next = null — первый узел отделён от списка.");
            b.Tail!.Next = first;
            b.Snap("tail.Next = first — первый узел прицеплен после хвоста.");
            b.Tail = first;
            b.Snap("tail = first — он стал новым хвостом. Идти по списку не понадобилось.");
            b.Relayout();
            b.Snap("Узлы выстроены по ссылкам: первый элемент перенесён в конец.");
        }
        else b.Snap("В списке один узел — переносить нечего.");
        frames.AddRange(b.Frames);
        return frames;
    }

    private static List<ListFrame> Distinct(IReadOnlyList<int> list)
    {
        var t = ListTrace.From(list);
        t.Snap("Исходный список. Идём по узлам и складываем значения в множество seen: повторы оно не примет.");
        var seen = new List<int>();
        for (var cur = t.Head; cur is not null; cur = cur.Next)
        {
            t.Mark("cur", cur);
            if (seen.Contains(cur.Value))
            {
                t.Doomed.Add(cur);
                t.Snap($"cur = {cur.Value}: такое значение уже есть в seen — повтор не считаем.");
            }
            else
            {
                seen.Add(cur.Value);
                t.Hot.Add(cur);
                t.Snap($"cur = {cur.Value}: новое значение, добавляем в seen = {{{string.Join(", ", seen)}}}.");
            }
        }
        t.Unmark("cur");
        t.Snap($"Конец списка. Различных элементов: {seen.Count}.");
        return t.Frames;
    }

    private static List<ListFrame> NonUnique(IReadOnlyList<int> list)
    {
        var t = ListTrace.From(list);
        t.Snap("Исходный список. Проход 1: считаем, сколько раз встречается каждое значение.");
        var counts = new Dictionary<int, int>();
        for (var cur = t.Head; cur is not null; cur = cur.Next)
        {
            counts[cur.Value] = counts.GetValueOrDefault(cur.Value) + 1;
            t.Mark("cur", cur);
            t.Hot.Add(cur);
            t.Snap($"cur = {cur.Value}: значение встретилось {counts[cur.Value]} раз.");
        }
        t.Unmark("cur");
        for (var cur = t.Head; cur is not null; cur = cur.Next)
            if (counts[cur.Value] > 1) t.Doomed.Add(cur);
        t.Snap("Проход 1 закончен. Красным выделены узлы, чьё значение встречается больше одного раза: их удалим все.");

        TNode? prev = null;
        for (var cur = t.Head; cur is not null;)
        {
            var next = cur.Next;
            t.Mark("prev", prev);
            t.Mark("cur", cur);
            if (counts[cur.Value] > 1)
            {
                if (prev is null) t.Head = next;
                else prev.Next = next;
                t.Doomed.Add(cur);
                t.Snap(prev is null
                    ? $"cur = {cur.Value} неуникальный и стоит в голове: head = cur.Next."
                    : $"cur = {cur.Value} неуникальный: prev.Next = cur.Next — ссылка идёт мимо узла.");
                t.Remove(cur);
                t.Snap("Узел исключён из списка. prev остаётся прежним.");
            }
            else
            {
                t.Hot.Add(cur);
                t.Snap($"cur = {cur.Value} уникальный — оставляем. prev = cur.");
                prev = cur;
            }
            cur = next;
        }
        t.Tail = prev;
        t.Unmark("cur");
        t.Mark("prev", prev);
        t.Snap("Конец списка. tail = prev — последний оставленный узел.");
        t.Relayout();
        t.Snap("Остались только уникальные элементы.");
        return t.Frames;
    }

    private static List<ListFrame> InsertSelf(IReadOnlyList<int> list, int x)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный список. Ищем первое вхождение x = {x}.");
        TNode? target = null;
        for (var cur = t.Head; cur is not null; cur = cur.Next)
        {
            t.Mark("cur", cur);
            if (cur.Value == x)
            {
                target = cur;
                t.Hot.Add(cur);
                t.Snap($"cur = {x} — нашли первое вхождение.");
                break;
            }
            t.Snap($"cur = {cur.Value} ≠ {x} — идём дальше.");
        }
        t.Unmark("cur");
        if (target is null)
        {
            t.Snap($"Числа {x} в списке нет — список не меняется.");
            return t.Frames;
        }

        t.Mark("x", target);
        t.Head2Label = "copy";
        TNode? copyTail = null;
        foreach (var value in list)
        {
            var node = t.Add(value, 1);
            if (copyTail is null) t.Head2 = node;
            else copyTail.Next = node;
            copyTail = node;
            t.Mark("copyTail", copyTail);
            t.Hot.Add(node);
            t.Snap($"Строим копию списка: добавляем узел со значением {value}. Вставлять исходный список в самого себя нельзя — получится цикл.");
        }
        var copyHead = t.Head2!;
        copyTail!.Next = target.Next;
        t.Snap("copyTail.Next = x.Next — хвост копии подхватывает продолжение исходного списка.");
        target.Next = copyHead;
        t.Snap("x.Next = copy — голова копии встала сразу после x.");
        if (target == t.Tail) t.Tail = copyTail;
        t.Head2 = null;
        t.Unmark("copyTail");
        t.Relayout();
        t.Snap(t.Tail == copyTail
            ? "Копия вошла в список: все узлы образуют одну цепочку, хвостом стал хвост копии."
            : "Копия вошла в список: все узлы образуют одну цепочку.");
        return t.Frames;
    }

    private static List<ListFrame> InsertSorted(IReadOnlyList<int> list, int e)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный упорядоченный список. Вставляем E = {e} так, чтобы порядок не нарушился.");
        var node = t.Add(e, 1);
        t.Mark("new", node);
        t.Hot.Add(node);
        t.Snap("Создаём новый узел со значением E.");
        if (t.Head is null || e < t.Head.Value)
        {
            node.Next = t.Head;
            t.Snap($"E = {e} меньше значения головы: new.Next = head.");
            t.Head = node;
            t.Tail ??= node;
            t.Relayout();
            t.Snap("head = new — новый узел стал головой.");
            return t.Frames;
        }

        var cur = t.Head;
        t.Mark("cur", cur);
        t.Snap("E не меньше головы. Ищем место: идём, пока значение cur.Next не больше E.");
        while (cur.Next is not null && cur.Next.Value <= e)
        {
            var value = cur.Next.Value;
            cur = cur.Next;
            t.Mark("cur", cur);
            t.Snap($"Следующий узел {value} ≤ {e} — сдвигаем cur.");
        }
        t.Snap(cur.Next is null ? "Дошли до конца: новый узел станет последним." : $"Следующий узел {cur.Next.Value} > {e} — вставляем после cur.");
        node.Next = cur.Next;
        t.Snap("new.Next = cur.Next — новый узел смотрит на продолжение списка.");
        cur.Next = node;
        if (cur == t.Tail) t.Tail = node;
        t.Snap("cur.Next = new — предыдущий узел теперь указывает на новый.");
        t.Relayout();
        t.Snap("Новый узел встал на своё место, порядок сохранён.");
        return t.Frames;
    }

    private static List<ListFrame> RemoveAll(IReadOnlyList<int> list, int e)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный список. Удаляем все узлы со значением E = {e}.");
        TNode? prev = null;
        var removed = 0;
        for (var cur = t.Head; cur is not null;)
        {
            var next = cur.Next;
            t.Mark("prev", prev);
            t.Mark("cur", cur);
            if (cur.Value == e)
            {
                if (prev is null) t.Head = next;
                else prev.Next = next;
                removed++;
                t.Doomed.Add(cur);
                t.Snap(prev is null
                    ? $"cur = {e} и стоит в голове: head = cur.Next."
                    : $"cur = {e}: prev.Next = cur.Next — ссылка идёт мимо узла.");
                t.Remove(cur);
                t.Snap("Узел исключён из списка. prev остаётся прежним.");
            }
            else
            {
                t.Snap($"cur = {cur.Value} ≠ {e} — оставляем. prev = cur.");
                prev = cur;
            }
            cur = next;
        }
        t.Tail = prev;
        t.Unmark("cur");
        t.Mark("prev", prev);
        t.Snap("Конец списка. tail = prev — последний оставленный узел.");
        t.Relayout();
        t.Snap($"Удалено узлов: {removed}.");
        return t.Frames;
    }

    private static List<ListFrame> InsertBefore(IReadOnlyList<int> list, int e, int f)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный список. Вставляем F = {f} перед первым E = {e}.");
        t.Mark("cur", t.Head);
        if (t.Head!.Value == e)
        {
            t.Hot.Add(t.Head);
            var first = t.Add(f, 1);
            t.Mark("new", first);
            t.Snap($"E = {e} стоит в голове — вставка в начало. Создаём узел F.");
            first.Next = t.Head;
            t.Snap("new.Next = head.");
            t.Head = first;
            t.Relayout();
            t.Snap("head = new — новый узел стал головой.");
            return t.Frames;
        }
        for (var cur = t.Head; cur.Next is not null; cur = cur.Next)
        {
            t.Mark("cur", cur);
            if (cur.Next.Value != e)
            {
                t.Snap($"cur.Next = {cur.Next.Value} ≠ {e} — идём дальше. Вставлять нужно после узла, стоящего перед E.");
                continue;
            }
            t.Hot.Add(cur.Next);
            var node = t.Add(f, 1);
            t.Mark("new", node);
            t.Snap($"cur.Next = {e} — нашли. Создаём узел F = {f}.");
            node.Next = cur.Next;
            t.Snap("new.Next = cur.Next — новый узел смотрит на E.");
            cur.Next = node;
            t.Snap("cur.Next = new — предыдущий узел теперь указывает на новый.");
            t.Relayout();
            t.Snap("Узел F встал перед первым E.");
            return t.Frames;
        }
        t.Snap($"Числа {e} в списке нет — список не меняется.");
        return t.Frames;
    }

    private static List<ListFrame> AppendList(IReadOnlyList<int> list, IReadOnlyList<int> second)
    {
        var t = ListTrace.From(list);
        t.HeadLabel = "head L";
        t.Head2Label = "head E";
        t.BuildSecond(second);
        t.Snap("Списки L (сверху) и E (снизу). Значения E по одному копируются в конец L.");
        for (var e = t.Head2; e is not null; e = e.Next)
        {
            t.Mark("e", e);
            var node = t.Add(e.Value);
            if (t.Tail is null) t.Head = node;
            else t.Tail.Next = node;
            t.Tail = node;
            t.Hot.Add(node);
            t.Snap($"e = {e.Value}: создаём копию узла и присоединяем к концу L (tail.Next = new, tail = new).");
        }
        t.Unmark("e");
        t.Snap("E дописан к L. Сам список E не изменился.");
        return t.Frames;
    }

    private static List<ListFrame> Split(IReadOnlyList<int> list, int x)
    {
        var t = ListTrace.From(list);
        TNode? prev = null, cur = t.Head;
        t.Mark("prev", prev);
        t.Mark("cur", cur);
        t.Snap($"Исходный список. Ищем первое вхождение x = {x}: cur = head, prev = null.");
        while (cur is not null && cur.Value != x)
        {
            var value = cur.Value;
            prev = cur;
            cur = cur.Next;
            t.Mark("prev", prev);
            t.Mark("cur", cur);
            t.Snap($"{value} ≠ {x} — сдвигаем prev и cur.");
        }
        if (cur is null)
        {
            t.Snap($"Числа {x} в списке нет — второй список пуст, первый не меняется.");
            return t.Frames;
        }

        var oldTail = t.Tail;
        t.Hot.Add(cur);
        t.Head2 = cur;
        t.Mark("tail₂", oldTail);
        t.Snap($"Нашли x = {x}. head₂ = cur: второй список начинается здесь, его хвост — прежний tail.");
        if (prev is null) t.Head = null;
        else prev.Next = null;
        t.Snap(prev is null ? "x стоит в голове: head = null — первый список пуст." : "prev.Next = null — ссылка перерезана, список разделён на два.");
        t.Tail = prev;
        t.Relayout();
        t.Snap("tail = prev. Второй список показан отдельной строкой. Новые узлы не создавались.");
        return t.Frames;
    }

    private static List<ListFrame> Double(IReadOnlyList<int> list)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный список. Дописываем в конец копии всех исходных значений — ровно {list.Count} штук: список растёт на ходу, поэтому цикл идёт по Count, а не до конца.");
        var source = t.Head;
        for (var i = 0; i < list.Count; i++)
        {
            t.Mark("src", source);
            var node = t.Add(source!.Value);
            t.Tail!.Next = node;
            t.Tail = node;
            t.Hot.Add(node);
            t.Snap($"src = {source.Value}: копия значения присоединена к концу (tail.Next = new, tail = new).");
            source = source.Next;
        }
        t.Unmark("src");
        t.Snap($"Список удвоен: было {list.Count} узлов, стало {list.Count * 2}.");
        return t.Frames;
    }

    private static List<ListFrame> Swap(IReadOnlyList<int> list, int x, int y)
    {
        var t = ListTrace.From(list);
        t.Snap($"Исходный список. Меняем местами узлы с первыми вхождениями {x} и {y}.");
        int ix = list.ToList().IndexOf(x), iy = list.ToList().IndexOf(y);
        if (ix < 0 || iy < 0)
        {
            t.Snap($"Чисел {x} и {y} в списке нет одновременно — список не меняется.");
            return t.Frames;
        }
        if (ix == iy)
        {
            t.Snap("Это один и тот же узел — менять нечего.");
            return t.Frames;
        }

        var chain = t.Chain();
        int first = Math.Min(ix, iy), second = Math.Max(ix, iy);
        var a = chain[first];
        var b = chain[second];
        var prevA = first > 0 ? chain[first - 1] : null;
        var prevB = chain[second - 1];
        t.Hot.Add(a);
        t.Hot.Add(b);
        t.Mark("a", a);
        t.Mark("b", b);
        t.Mark("prevA", prevA);
        t.Mark("prevB", prevB);
        t.Snap($"Нашли оба узла: a = {a.Value} (левее) и b = {b.Value} (правее), а также узлы перед ними — prevA и prevB.");

        if (prevA is null) t.Head = b;
        else prevA.Next = b;
        t.Snap(prevA is null ? "a — голова: head = b." : "prevA.Next = b — b встаёт на место a.");

        var afterB = b.Next;
        if (prevB == a)
        {
            b.Next = a;
            t.Snap("Узлы соседние: b.Next = a.");
        }
        else
        {
            b.Next = a.Next;
            t.Snap("b.Next = a.Next — b получает продолжение, которое шло за a.");
            prevB.Next = a;
            t.Snap("prevB.Next = a — a встаёт на место b.");
        }
        a.Next = afterB;
        if (afterB is null) t.Tail = a;
        t.Snap(afterB is null ? "a.Next = null, tail = a — a ушёл в конец." : "a.Next = то, что шло за b.");
        t.Relayout();
        t.Snap("Узлы выстроены по ссылкам: a и b поменялись местами, значения внутри узлов не копировались.");
        return t.Frames;
    }
}
