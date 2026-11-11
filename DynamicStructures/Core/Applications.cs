using System.IO;
using System.Text;

namespace DynamicStructures.Core;

/// <summary>Пример применения структуры данных (часть 3): читает команды из файла и возвращает отчёт «итог, потом подробности».</summary>
public sealed record Demo(string Title, string Sample, Func<string, List<AnimationScript>?, string> Run, bool Animated = false);

public static class Demos
{
    public static readonly Demo[] All =
    [
        new("Список: хеш-таблица с цепочками", "hashtable.txt", (path, _) => HashTableDemo.Run(path)),
        new("Стек: стек вызовов (имитация рекурсии)", "callstack.txt", CallStackDemo.Run, true),
        new("Очередь: очередь печати с приоритетами", "printqueue.txt", (path, _) => PrintQueueDemo.Run(path)),
        new("Дерево: двоичная куча (пирамидальная сортировка)", "heap.txt", (path, _) => HeapDemo.Run(path)),
    ];

    internal static string Compose(StringBuilder summary, StringBuilder details) =>
        $"=== Итог ==={Environment.NewLine}{summary}{Environment.NewLine}=== Подробно ==={Environment.NewLine}{details}";

    /// <summary>Непустые строки файла без комментариев (строки, начинающиеся с #), с их номерами.</summary>
    internal static List<(int Number, string Text)> ReadCommands(string path)
    {
        var lines = File.ReadAllLines(path)
            .Select((text, i) => (Number: i + 1, Text: text.Trim()))
            .Where(l => l.Text.Length > 0 && !l.Text.StartsWith('#'))
            .ToList();
        return lines.Count > 0 ? lines : throw new FormatException("Файл пуст.");
    }
}

/// <summary>Хеш-таблица, в которой коллизии разрешаются цепочками — односвязными списками узлов в каждой корзине.</summary>
public sealed class ChainedHashTable
{
    private sealed class Entry(string key, string value)
    {
        public string Key { get; } = key;
        public string Value { get; set; } = value;
    }

    private Node<Entry>?[] _buckets;

    public ChainedHashTable(int capacity = 4) => _buckets = new Node<Entry>?[capacity];

    public int Count { get; private set; }
    public int Capacity => _buckets.Length;
    public int Resizes { get; private set; }
    public double LoadFactor => (double)Count / Capacity;

    public int LongestChain => _buckets.Max(Length);

    /// <summary>Своя детерминированная хеш-функция (string.GetHashCode меняется от запуска к запуску).</summary>
    public static int Hash(string key, int capacity)
    {
        uint hash = 17;
        foreach (var c in key) hash = unchecked(hash * 31 + c);
        return (int)(hash % (uint)capacity);
    }

    public string Put(string key, string value)
    {
        var index = Hash(key, Capacity);
        for (var node = _buckets[index]; node is not null; node = node.Next)
        {
            if (node.Value.Key != key) continue;
            var old = node.Value.Value;
            node.Value.Value = value;
            return $"ключ уже есть, значение обновлено (было {old}); корзина {index}";
        }

        var note = "";
        if (Count + 1 > Capacity * 0.75)
        {
            var before = Capacity;
            Resize();
            index = Hash(key, Capacity);
            note = $"; перехеширование: корзин {before} → {Capacity}";
        }
        var collision = _buckets[index] is not null;
        _buckets[index] = new Node<Entry>(new Entry(key, value)) { Next = _buckets[index] };
        Count++;
        return collision
            ? $"корзина {index} уже занята — коллизия, ключ встал в цепочку (длина {Length(_buckets[index])}){note}"
            : $"добавлено в пустую корзину {index}{note}";
    }

    public string Get(string key)
    {
        var index = Hash(key, Capacity);
        var steps = 0;
        for (var node = _buckets[index]; node is not null; node = node.Next)
        {
            steps++;
            if (node.Value.Key == key) return $"найдено: {node.Value.Value} (корзина {index}, просмотрено узлов: {steps})";
        }
        return $"не найдено (корзина {index}, просмотрено узлов: {steps})";
    }

    public string Remove(string key)
    {
        var index = Hash(key, Capacity);
        Node<Entry>? prev = null;
        for (var node = _buckets[index]; node is not null; prev = node, node = node.Next)
        {
            if (node.Value.Key != key) continue;
            if (prev is null) _buckets[index] = node.Next;
            else prev.Next = node.Next;
            Count--;
            return $"удалён (корзина {index})";
        }
        return $"такого ключа нет (корзина {index})";
    }

    public IEnumerable<string> Dump()
    {
        for (var i = 0; i < _buckets.Length; i++)
        {
            var items = new List<string>();
            for (var node = _buckets[i]; node is not null; node = node.Next) items.Add($"({node.Value.Key} = {node.Value.Value})");
            yield return $"[{i}] {(items.Count == 0 ? "—" : string.Join(" → ", items))}";
        }
    }

    private static int Length(Node<Entry>? head)
    {
        var length = 0;
        for (var node = head; node is not null; node = node.Next) length++;
        return length;
    }

    private void Resize()
    {
        var old = _buckets;
        _buckets = new Node<Entry>?[old.Length * 2];
        Resizes++;
        foreach (var head in old)
        {
            for (var node = head; node is not null;)
            {
                var next = node.Next;
                var index = Hash(node.Value.Key, _buckets.Length);
                node.Next = _buckets[index];
                _buckets[index] = node;
                node = next;
            }
        }
    }
}

public static class HashTableDemo
{
    public static string Run(string path)
    {
        var table = new ChainedHashTable();
        var details = new StringBuilder();
        var total = 0;
        var errors = 0;
        foreach (var (number, line) in Demos.ReadCommands(path))
        {
            total++;
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                var result = parts[0].ToLowerInvariant() switch
                {
                    "put" when parts.Length == 3 => table.Put(parts[1], parts[2]),
                    "get" when parts.Length == 2 => table.Get(parts[1]),
                    "remove" when parts.Length == 2 => table.Remove(parts[1]),
                    "print" when parts.Length == 1 => Environment.NewLine + string.Join(Environment.NewLine, table.Dump().Select(l => "      " + l)),
                    _ => throw new FormatException("ожидается put ключ значение, get ключ, remove ключ или print"),
                };
                details.AppendLine($"{number}. {line} → {result}");
            }
            catch (FormatException ex)
            {
                errors++;
                details.AppendLine($"{number}. {line} → Ошибка: {ex.Message}");
            }
        }

        var summary = new StringBuilder()
            .AppendLine($"Команд: {total}, с ошибками: {errors}.")
            .AppendLine($"Элементов: {table.Count}, корзин: {table.Capacity}, коэффициент заполнения: {table.LoadFactor:0.##}.")
            .AppendLine($"Самая длинная цепочка: {table.LongestChain}, перехеширований: {table.Resizes}.")
            .AppendLine("Итоговая таблица:");
        foreach (var bucket in table.Dump()) summary.AppendLine("  " + bucket);
        return Demos.Compose(summary, details);
    }
}

/// <summary>Рекурсивные функции без рекурсии: вызовы и возвраты выполняются на собственном стеке кадров.</summary>
public static class CallStackDemo
{
    private const int MaxTraceLines = 150;
    private const int MaxAnimatedSteps = 100;

    private sealed class Frame(string kind, int n)
    {
        public string Kind { get; } = kind;
        public int N { get; } = n;
        public int Stage { get; set; }
        public long Partial { get; set; }
        public string Label => $"{Kind}({N})";
    }

    public static string Run(string path, List<AnimationScript>? scripts)
    {
        var summary = new StringBuilder();
        var details = new StringBuilder();
        var total = 0;
        var errors = 0;
        foreach (var (number, line) in Demos.ReadCommands(path))
        {
            total++;
            details.AppendLine($"Строка {number}: {line}");
            try
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !int.TryParse(parts[1], out var n)) throw new FormatException("ожидается «fact N» или «fib N»");
                var kind = parts[0].ToLowerInvariant();
                var limit = kind switch
                {
                    "fact" => 20,
                    "fib" => 30,
                    _ => throw new FormatException($"неизвестная функция «{parts[0]}», доступны fact и fib"),
                };
                if (n < 0 || n > limit) throw new FormatException($"N должно быть от 0 до {limit}");

                var trace = new StringBuilder();
                var traced = 0;
                var ops = new List<Operation>();
                var result = Simulate(kind, n, text =>
                {
                    if (traced < MaxTraceLines) trace.AppendLine("  " + text);
                    else if (traced == MaxTraceLines) trace.AppendLine("  … (остальные вызовы не показаны)");
                    traced++;
                }, ops, out var calls, out var depth);

                details.Append(trace).AppendLine($"  Результат: {result}; вызовов: {calls}; максимальная глубина стека: {depth}").AppendLine();
                summary.AppendLine($"  {number}: {kind}({n}) = {result}  (вызовов: {calls}, макс. глубина стека: {depth})");
                scripts?.Add(new(ops.Count >= MaxAnimatedSteps ? $"{kind}({n}), первые {MaxAnimatedSteps} шагов" : $"{kind}({n})", ops));
            }
            catch (Exception ex) when (ex is FormatException or ArithmeticException)
            {
                errors++;
                details.AppendLine($"  Ошибка: {ex.Message}").AppendLine();
                summary.AppendLine($"  {number}: {line}  →  ошибка: {ex.Message}");
            }
        }
        summary.Insert(0, $"Обработано строк: {total}, с ошибками: {errors}.{Environment.NewLine}");
        return Demos.Compose(summary, details);
    }

    private static long Simulate(string kind, int n, Action<string> trace, List<Operation> ops, out int calls, out int maxDepth)
    {
        var stack = new LinkedStack<Frame>();
        long ret = 0;
        var callCount = 0;
        var deepest = 0;

        void Call(string k, int m)
        {
            var frame = new Frame(k, m);
            stack.Push(frame);
            callCount++;
            deepest = Math.Max(deepest, stack.Count);
            trace($"{new string(' ', 2 * (stack.Count - 1))}→ вызов {frame.Label}");
            if (ops.Count < MaxAnimatedSteps) ops.Add(new(1, frame.Label, $"вызов {frame.Label}: кадр кладётся в стек"));
        }

        void Return(Frame frame, long value)
        {
            ret = value;
            trace($"{new string(' ', 2 * (stack.Count - 1))}← {frame.Label} = {value}");
            stack.Pop();
            if (ops.Count < MaxAnimatedSteps) ops.Add(new(2, null, $"{frame.Label} вернул {value}: кадр снимается со стека"));
        }

        Call(kind, n);
        while (!stack.IsEmpty())
        {
            var frame = stack.Top();
            if (frame.Kind == "fact")
            {
                if (frame.Stage == 0)
                {
                    if (frame.N <= 1) Return(frame, 1);
                    else { frame.Stage = 1; Call("fact", frame.N - 1); }
                }
                else Return(frame, checked(frame.N * ret));
            }
            else if (frame.Stage == 0)
            {
                if (frame.N < 2) Return(frame, frame.N);
                else { frame.Stage = 1; Call("fib", frame.N - 1); }
            }
            else if (frame.Stage == 1)
            {
                frame.Partial = ret;
                frame.Stage = 2;
                Call("fib", frame.N - 2);
            }
            else Return(frame, frame.Partial + ret);
        }
        calls = callCount;
        maxDepth = deepest;
        return ret;
    }
}

/// <summary>Очередь печати: три очереди по приоритетам на собственной очереди, отмена задания перестройкой очереди.</summary>
public static class PrintQueueDemo
{
    private static readonly string[] PriorityNames = ["высокий", "обычный", "низкий"];

    private sealed class Job(string name, int pages, int priority, int submitted)
    {
        public string Name { get; } = name;
        public int Pages { get; } = pages;
        public int Priority { get; } = priority;
        public int Submitted { get; } = submitted;
        public int Printed { get; set; }
        public int Started { get; set; } = -1;
    }

    private sealed class Spooler
    {
        private readonly LinkedQueue<Job>[] _queues = [new(), new(), new()];

        public Job? Current { get; private set; }
        public int Clock { get; private set; }
        public int Cancelled { get; private set; }
        public int Pages { get; private set; }
        public List<(Job Job, int Wait)> Done { get; } = [];
        public bool Busy => Current is not null || _queues.Any(q => !q.IsEmpty());
        public int Waiting => _queues.Sum(q => Snapshot(q).Count) + (Current is null ? 0 : 1);

        public void Add(string name, int pages, int priority) => _queues[priority].Enqueue(new Job(name, pages, priority, Clock));

        public bool Cancel(string name)
        {
            if (Current?.Name == name)
            {
                Current = null;
                Cancelled++;
                return true;
            }
            var found = false;
            foreach (var queue in _queues)
            {
                var kept = new List<Job>();
                while (!queue.IsEmpty())
                {
                    var job = queue.Dequeue();
                    if (!found && job.Name == name) found = true;
                    else kept.Add(job);
                }
                foreach (var job in kept) queue.Enqueue(job);
            }
            if (found) Cancelled++;
            return found;
        }

        public void Tick(int pages, List<string> log)
        {
            for (var i = 0; i < pages; i++)
            {
                Current ??= _queues.FirstOrDefault(q => !q.IsEmpty())?.Dequeue();
                if (Current is null)
                {
                    log.Add("принтер свободен, очередь пуста");
                    return;
                }
                if (Current.Started < 0)
                {
                    Current.Started = Clock;
                    log.Add($"начата печать «{Current.Name}» ({Current.Pages} стр., ожидала {Clock - Current.Submitted} тактов)");
                }
                Current.Printed++;
                Clock++;
                Pages++;
                if (Current.Printed < Current.Pages) continue;
                Done.Add((Current, Current.Started - Current.Submitted));
                log.Add($"напечатано «{Current.Name}»");
                Current = null;
            }
        }

        public List<string> State()
        {
            var lines = new List<string>
            {
                Current is null ? "принтер свободен" : $"печатается: «{Current.Name}» ({Current.Printed}/{Current.Pages} стр.)",
            };
            for (var p = 0; p < _queues.Length; p++)
            {
                var jobs = Snapshot(_queues[p]);
                lines.Add($"{PriorityNames[p]}: {(jobs.Count == 0 ? "—" : string.Join(" → ", jobs.Select(j => $"{j.Name} ({j.Pages} стр.)")))}");
            }
            return lines;
        }

        /// <summary>Содержимое очереди: достаём все задания и возвращаем их обратно в том же порядке.</summary>
        private static List<Job> Snapshot(LinkedQueue<Job> queue)
        {
            var all = new List<Job>();
            while (!queue.IsEmpty()) all.Add(queue.Dequeue());
            foreach (var job in all) queue.Enqueue(job);
            return all;
        }
    }

    public static string Run(string path)
    {
        var spooler = new Spooler();
        var details = new StringBuilder();
        var total = 0;
        var errors = 0;
        foreach (var (number, line) in Demos.ReadCommands(path))
        {
            total++;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var log = new List<string>();
            try
            {
                var result = parts[0].ToLowerInvariant() switch
                {
                    "add" when parts.Length is 3 or 4 => Add(spooler, parts),
                    "cancel" when parts.Length == 2 => spooler.Cancel(parts[1]) ? $"задание «{parts[1]}» отменено" : $"задания «{parts[1]}» нет в очереди",
                    "print" when parts.Length == 1 => "состояние очереди",
                    "tick" when parts.Length is 1 or 2 => Tick(spooler, parts, log),
                    "run" when parts.Length == 1 => Run(spooler, log),
                    _ => throw new FormatException("ожидается add имя страницы [приоритет], cancel имя, print, tick [N] или run"),
                };
                details.AppendLine($"{number}. {line} → {result}");
                foreach (var entry in log) details.AppendLine($"      {entry}");
                if (parts[0].Equals("print", StringComparison.OrdinalIgnoreCase))
                    foreach (var state in spooler.State()) details.AppendLine($"      {state}");
            }
            catch (FormatException ex)
            {
                errors++;
                details.AppendLine($"{number}. {line} → Ошибка: {ex.Message}");
            }
        }

        var average = spooler.Done.Count == 0 ? 0 : spooler.Done.Average(d => d.Wait);
        var summary = new StringBuilder()
            .AppendLine($"Команд: {total}, с ошибками: {errors}.")
            .AppendLine($"Напечатано документов: {spooler.Done.Count}, страниц: {spooler.Pages}, отменено: {spooler.Cancelled}, ещё ждут: {spooler.Waiting}.")
            .AppendLine($"Среднее ожидание начала печати: {average:0.##} тактов (такт = одна страница).")
            .AppendLine($"Порядок печати: {(spooler.Done.Count == 0 ? "—" : string.Join(" → ", spooler.Done.Select(d => $"{d.Job.Name} (ждал {d.Wait})")))}");
        return Demos.Compose(summary, details);
    }

    private static string Add(Spooler spooler, string[] parts)
    {
        if (!int.TryParse(parts[2], out var pages) || pages <= 0) throw new FormatException("число страниц должно быть целым и больше нуля");
        var priority = 1;
        if (parts.Length == 4)
        {
            priority = parts[3].ToLowerInvariant() switch
            {
                "high" or "высокий" or "1" => 0,
                "normal" or "обычный" or "2" => 1,
                "low" or "низкий" or "3" => 2,
                _ => throw new FormatException("приоритет: high, normal, low (или 1, 2, 3)"),
            };
        }
        spooler.Add(parts[1], pages, priority);
        return $"«{parts[1]}», {pages} стр., приоритет «{PriorityNames[priority]}» поставлено в очередь";
    }

    private static string Tick(Spooler spooler, string[] parts, List<string> log)
    {
        var pages = 1;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out pages) || pages <= 0)) throw new FormatException("N должно быть целым и больше нуля");
        spooler.Tick(pages, log);
        return $"принтер работает {pages} тактов";
    }

    private static string Run(Spooler spooler, List<string> log)
    {
        var start = spooler.Clock;
        while (spooler.Busy) spooler.Tick(1, log);
        return $"печать всей очереди заняла {spooler.Clock - start} тактов";
    }
}

/// <summary>Двоичная куча (максимум в корне), хранящаяся в массиве, и пирамидальная сортировка на ней.</summary>
public static class HeapDemo
{
    private const int MaxLogLines = 400;

    private sealed class MaxHeap
    {
        public List<int> Data { get; } = [];
        public int Comparisons { get; private set; }
        public int Swaps { get; private set; }

        public void Insert(int value, Action<string> log)
        {
            Data.Add(value);
            var i = Data.Count - 1;
            log($"вставка {value}: кладём в конец массива (индекс {i}) и поднимаем вверх");
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                Comparisons++;
                if (Data[i] <= Data[parent])
                {
                    log($"    {Data[i]} ≤ родителя {Data[parent]} — остаётся на месте");
                    break;
                }
                log($"    {Data[i]} > родителя {Data[parent]} — меняем местами (индексы {i} ↔ {parent})");
                (Data[i], Data[parent]) = (Data[parent], Data[i]);
                Swaps++;
                i = parent;
            }
        }

        public int ExtractMax(Action<string> log)
        {
            var root = Data[0];
            var last = Data[^1];
            Data.RemoveAt(Data.Count - 1);
            if (Data.Count == 0)
            {
                log($"извлекаем максимум {root}: куча опустела");
                return root;
            }
            Data[0] = last;
            log($"извлекаем максимум {root}: последний элемент {last} ставим в корень и опускаем вниз");
            var i = 0;
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, largest = i;
                if (left < Data.Count) { Comparisons++; if (Data[left] > Data[largest]) largest = left; }
                if (right < Data.Count) { Comparisons++; if (Data[right] > Data[largest]) largest = right; }
                if (largest == i)
                {
                    log($"    {Data[i]} не меньше потомков — остаётся на месте");
                    break;
                }
                log($"    {Data[i]} < потомка {Data[largest]} — меняем местами (индексы {i} ↔ {largest})");
                (Data[i], Data[largest]) = (Data[largest], Data[i]);
                Swaps++;
                i = largest;
            }
            return root;
        }

        public IEnumerable<string> Levels()
        {
            for (int level = 0, start = 0; start < Data.Count; level++, start = (1 << level) - 1)
            {
                var count = Math.Min(1 << level, Data.Count - start);
                yield return $"уровень {level}: {string.Join("  ", Data.Skip(start).Take(count))}";
            }
        }
    }

    public static string Run(string path)
    {
        var tokens = Demos.ReadCommands(path).SelectMany(l => l.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        var values = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            if (!int.TryParse(tokens[i], out values[i])) throw new FormatException($"Элемент №{i + 1} «{tokens[i]}» не является целым числом.");

        var details = new StringBuilder();
        var logged = 0;
        void Log(string text)
        {
            if (logged < MaxLogLines) details.AppendLine(text);
            else if (logged == MaxLogLines) details.AppendLine("… (остальные шаги не показаны)");
            logged++;
        }

        var heap = new MaxHeap();
        details.AppendLine("--- Построение кучи: вставляем элементы по одному ---");
        foreach (var value in values)
        {
            heap.Insert(value, Log);
            Log($"    куча: [{string.Join(", ", heap.Data)}]");
        }
        var built = heap.Levels().ToList();
        var builtArray = string.Join(", ", heap.Data);

        details.AppendLine().AppendLine("--- Сортировка: по одному извлекаем максимум ---");
        var sorted = new int[values.Length];
        for (var k = values.Length - 1; k >= 0; k--)
        {
            sorted[k] = heap.ExtractMax(Log);
            Log($"    куча: [{string.Join(", ", heap.Data)}]");
        }

        var height = values.Length == 0 ? 0 : (int)Math.Floor(Math.Log2(values.Length)) + 1;
        var summary = new StringBuilder()
            .AppendLine($"Исходный массив:        [{string.Join(", ", values)}]")
            .AppendLine($"Отсортированный массив: [{string.Join(", ", sorted)}]")
            .AppendLine($"Элементов: {values.Length}, высота кучи: {height}, сравнений: {heap.Comparisons}, обменов: {heap.Swaps}.")
            .AppendLine("Оценка сложности: построение и сортировка O(n log n), память O(n).")
            .AppendLine($"Куча после построения, массив: [{builtArray}]");
        foreach (var level in built) summary.AppendLine("  " + level);
        return Demos.Compose(summary, details);
    }
}
