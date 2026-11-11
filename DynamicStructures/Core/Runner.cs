using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DynamicStructures.Core;

public readonly record struct Operation(int Code, string? Argument, string? Note = null);

public sealed record Measurement(string Series, string File, int Operations, double ReadMs, double ExecMs);

/// <summary>Набор из пяти операций input.txt поверх конкретной структуры данных.</summary>
public abstract class OperationSet
{
    protected abstract string Insert(string value);
    protected abstract string Remove();
    protected abstract string Peek();
    protected abstract bool IsEmpty();
    protected abstract string Print();
    protected abstract string Name(int code);

    public string Apply(in Operation op) => op.Code switch
    {
        1 => Insert(op.Argument!),
        2 => IsEmpty() ? "пусто" : Remove(),
        3 => IsEmpty() ? "пусто" : Peek(),
        4 => IsEmpty() ? "да" : "нет",
        _ => Print(),
    };

    public string Describe(in Operation op) => op.Code == 1 ? $"{Name(1)}({op.Argument})" : $"{Name(op.Code)}()";
}

public sealed class StackOperations : OperationSet
{
    private readonly LinkedStack<string> _stack = new();
    protected override string Insert(string value) { _stack.Push(value); return "ok"; }
    protected override string Remove() => _stack.Pop();
    protected override string Peek() => _stack.Top();
    protected override bool IsEmpty() => _stack.IsEmpty();
    protected override string Print() => _stack.Print();
    protected override string Name(int code) => code switch
    { 1 => "Push", 2 => "Pop", 3 => "Top", 4 => "isEmpty", _ => "Print" };
}

public sealed class QueueOperations(IQueue<string> queue) : OperationSet
{
    protected override string Insert(string value) { queue.Enqueue(value); return "ok"; }
    protected override string Remove() => queue.Dequeue();
    protected override string Peek() => queue.Peek();
    protected override bool IsEmpty() => queue.IsEmpty();
    protected override string Print() => queue.Print();
    protected override string Name(int code) => code switch
    { 1 => "Вставка", 2 => "Удаление", 3 => "Начало", 4 => "Пуста?", _ => "Печать" };
}

public sealed record Implementation(string Series, Func<OperationSet> Create);

public static class Runner
{
    public const int Runs = 5;
    private const int MaxLogLines = 1000;
    private const int MaxLineLength = 200;

    public static readonly Implementation[] Stack = [new("Стек", () => new StackOperations())];

    public static readonly Implementation[] Queues =
    [
        new("Queue<T>", () => new QueueOperations(new StandardQueue<string>())),
        new("Свой список", () => new QueueOperations(new LinkedQueue<string>())),
        new("List<T>", () => new QueueOperations(new ListQueue<string>())),
    ];

    public static Operation[] Parse(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("Файл пуст.");

        var ops = new Operation[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("1,"))
                ops[i] = token.Length > 2 ? new(1, token[2..]) : throw ParseError(i, token, "не указано значение после запятой");
            else if (token is "2" or "3" or "4" or "5")
                ops[i] = new(token[0] - '0', null);
            else if (token == "1")
                throw ParseError(i, token, "для операции 1 нужно значение: 1,значение");
            else
                throw ParseError(i, token, "неизвестная операция, допустимы 1,значение и 2–5");
        }
        return ops;
    }

    private static FormatException ParseError(int index, string token, string message) =>
        new($"Операция №{index + 1} «{token}»: {message}.");

    public static IReadOnlyList<string> ResolveFiles(string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.txt").Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return files.Length > 0 ? files : throw new FileNotFoundException($"В папке нет .txt файлов: {path}");
        }
        return File.Exists(path) ? [path] : throw new FileNotFoundException($"Путь не найден: {path}");
    }

    /// <summary>
    /// Для каждого файла: лог операций первой реализации и по <see cref="Runs"/> прогонов
    /// чтения и выполнения каждой реализации; в результат идёт минимальное время.
    /// </summary>
    public static List<Measurement> Measure(IReadOnlyList<string> files, IReadOnlyList<Implementation> implementations,
        StringBuilder log, IProgress<double>? progress, CancellationToken ct)
    {
        var results = new List<Measurement>();
        var totalSteps = files.Count * implementations.Count * Runs;
        var step = 0;

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            Operation[] ops = [];
            var readMs = double.MaxValue;
            try
            {
                for (var run = 0; run < Runs; run++)
                {
                    var sw = Stopwatch.StartNew();
                    ops = Parse(File.ReadAllText(path));
                    readMs = Math.Min(readMs, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (FormatException ex)
            {
                log.AppendLine($"=== {name} ===").AppendLine($"Ошибка: {ex.Message}").AppendLine("Файл пропущен.").AppendLine();
                step += implementations.Count * Runs;
                progress?.Report((double)step / totalSteps);
                continue;
            }

            log.AppendLine($"=== {name}: {ops.Length} операций ===");
            Execute(ops, implementations[0].Create(), log, ct);

            foreach (var impl in implementations)
            {
                var execMs = double.MaxValue;
                for (var run = 0; run < Runs; run++)
                {
                    var set = impl.Create();
                    var sw = Stopwatch.StartNew();
                    Execute(ops, set, null, ct);
                    execMs = Math.Min(execMs, sw.Elapsed.TotalMilliseconds);
                    progress?.Report((double)++step / totalSteps);
                }
                results.Add(new(impl.Series, name, ops.Length, readMs, execMs));
                log.AppendLine($"{impl.Series}: считывание {readMs:0.###} мс, выполнение {execMs:0.###} мс (минимум из {Runs})");
            }
            log.AppendLine();
        }

        if (results.Count > 0)
        {
            var summary = new StringBuilder().AppendLine("=== Итог: время, мс ===");
            foreach (var r in results)
                summary.AppendLine($"{r.File,-30} {r.Operations,7} оп. | {r.Series,-12} | считывание {r.ReadMs,9:0.###} | выполнение {r.ExecMs,9:0.###}");
            summary.AppendLine().AppendLine("=== Подробно: операции и время по каждому набору ===");
            log.Insert(0, summary.ToString());
        }
        return results;
    }

    private static void Execute(Operation[] ops, OperationSet set, StringBuilder? log, CancellationToken ct)
    {
        for (var i = 0; i < ops.Length; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            var result = set.Apply(ops[i]);
            if (log is null || i >= MaxLogLines) continue;
            if (result.Length > MaxLineLength) result = result[..MaxLineLength] + " …";
            log.Append(i + 1).Append(". ").Append(set.Describe(ops[i])).Append(" → ").AppendLine(result);
        }
        if (log is not null && ops.Length > MaxLogLines)
            log.AppendLine($"… ещё {ops.Length - MaxLogLines} операций не показаны");
    }

    /// <summary>Часть 4: читает целые числа из файла и применяет к копиям списка четыре алгоритма.</summary>
}

/// <summary>Входные данные алгоритма: список L (первая строка файла), список E (вторая строка) и числа из поля параметров.</summary>
public sealed record ListInput(SinglyLinkedList<int> List, SinglyLinkedList<int>? Second, int[] Args);

/// <summary><paramref name="Parameters"/> — имена параметров через пробел, <paramref name="Defaults"/> — их значения по умолчанию.</summary>
public sealed record ListAlgorithm(string Title, Func<ListInput, string> Run, string Parameters = "", string Defaults = "", string Sample = "list.txt");

/// <summary>Алгоритмы части 4. Новый алгоритм — одна строка в <see cref="All"/>, пункт меню появится сам.</summary>
public static class ListAlgorithms
{
    public static readonly ListAlgorithm[] All =
    [
        new("Переворот списка", x => { x.List.Reverse(); return x.List.ToString(); }),
        new("Последний в начало, первый в конец", x =>
        {
            var copy = new SinglyLinkedList<int>(x.List);
            x.List.MoveLastToFront();
            copy.MoveFirstToBack();
            return $"последний в начало: {x.List}; первый в конец: {copy}";
        }),
        new("Количество различных элементов", x => x.List.CountDistinct().ToString()),
        new("Удаление неуникальных элементов", x => { x.List.RemoveNonUnique(); return x.List.ToString(); }),
        new("Вставка списка в себя после x", x =>
            x.List.InsertSelfAfter(x.Args[0]) ? x.List.ToString() : $"{x.List} (числа {x.Args[0]} в списке нет)", "x", "3"),
        new("Вставка E в упорядоченный список", x =>
        {
            if (!x.List.IsSorted()) throw new FormatException("Список в файле не упорядочен по неубыванию.");
            x.List.InsertSorted(x.Args[0]);
            return x.List.ToString();
        }, "E", "6", "list_sorted.txt"),
        new("Удаление всех элементов E", x =>
        {
            var removed = x.List.RemoveAll(x.Args[0]);
            return $"{x.List} (удалено: {removed})";
        }, "E", "4"),
        new("Вставка F перед первым E", x =>
            x.List.InsertBefore(x.Args[0], x.Args[1]) ? x.List.ToString() : $"{x.List} (числа {x.Args[0]} в списке нет)", "E F", "4 99"),
        new("Дописать список E к списку L", x =>
        {
            var second = x.Second ?? throw new FormatException("Во второй строке файла должен быть список E.");
            x.List.Append(second);
            return $"{x.List} (E: {second})";
        }, Sample: "list_two.txt"),
        new("Разбиение по первому вхождению x", x =>
        {
            var second = x.List.SplitAt(x.Args[0]);
            return $"первый: {x.List}; второй: {second}";
        }, "x", "5"),
        new("Удвоение списка", x => { x.List.Append(x.List); return x.List.ToString(); }),
        new("Обмен двух элементов местами", x =>
            x.List.SwapValues(x.Args[0], x.Args[1])
                ? x.List.ToString()
                : $"{x.List} (чисел {x.Args[0]} и {x.Args[1]} в списке нет одновременно)", "x y", "2 9"),
    ];

    public static string Run(ListAlgorithm algorithm, string path, string parameters = "")
    {
        var lists = Read(path);
        var names = algorithm.Parameters.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var args = ParseArgs(names, parameters);
        var source = lists[0].ToString();
        var result = algorithm.Run(new(lists[0], lists.Count > 1 ? lists[1] : null, args));
        var report = new StringBuilder().AppendLine($"Результат:       {result}");
        if (names.Length > 0)
            report.AppendLine($"Параметры:       {string.Join(", ", names.Zip(args, (name, value) => $"{name} = {value}"))}");
        return report.Append($"Исходный список: {source}").ToString();
    }

    private static int[] ParseArgs(string[] names, string text)
    {
        var tokens = text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var args = new int[names.Length];
        if (tokens.Length != names.Length || !tokens.Select((t, i) => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out args[i])).All(ok => ok))
            throw new FormatException($"В поле параметров нужно указать целые числа через пробел: {string.Join(' ', names)}.");
        return args;
    }

    /// <summary>Каждая непустая строка файла — отдельный список целых чисел.</summary>
    public static List<SinglyLinkedList<int>> Read(string path)
    {
        var lists = new List<SinglyLinkedList<int>>();
        foreach (var line in File.ReadLines(path))
        {
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var list = new SinglyLinkedList<int>();
            for (var i = 0; i < tokens.Length; i++)
                list.AddLast(int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : throw new FormatException($"Строка {lists.Count + 1}, элемент №{i + 1} «{tokens[i]}» не является целым числом."));
            lists.Add(list);
        }
        return lists.Count > 0 ? lists : throw new FormatException("Файл пуст.");
    }
}
