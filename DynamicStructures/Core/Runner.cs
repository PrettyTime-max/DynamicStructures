using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DynamicStructures.Core;

public readonly record struct Operation(int Code, string? Argument);

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
            for (var run = 0; run < Runs; run++)
            {
                var sw = Stopwatch.StartNew();
                ops = Parse(File.ReadAllText(path));
                readMs = Math.Min(readMs, sw.Elapsed.TotalMilliseconds);
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
    public static string RunListAlgorithms(string path)
    {
        var tokens = File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("Файл пуст.");
        var numbers = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                throw new FormatException($"Элемент №{i + 1} «{tokens[i]}» не является целым числом.");

        SinglyLinkedList<int> Copy() => new(numbers);
        var reversed = Copy(); reversed.Reverse();
        var lastToFront = Copy(); lastToFront.MoveLastToFront();
        var firstToBack = Copy(); firstToBack.MoveFirstToBack();
        var unique = Copy(); unique.RemoveNonUnique();

        return new StringBuilder()
            .AppendLine($"Исходный список:        {Copy()}")
            .AppendLine($"1. Переворот:            {reversed}")
            .AppendLine($"2. Последний в начало:   {lastToFront}")
            .AppendLine($"   Первый в конец:       {firstToBack}")
            .AppendLine($"3. Различных элементов:  {Copy().CountDistinct()}")
            .AppendLine($"4. Без неуникальных:     {unique}")
            .ToString();
    }
}

/// <summary>Хранит замеры в CSV, чтобы график переживал перезапуск приложения.</summary>
public sealed class MeasurementStore
{
    private readonly string _path;
    public List<Measurement> Items { get; } = [];

    public MeasurementStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var p = line.Split(';');
            if (p.Length == 5 && int.TryParse(p[2], out var ops)
                && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var read)
                && double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var exec))
                Items.Add(new(p[0], p[1], ops, read, exec));
        }
    }

    public void Merge(IEnumerable<Measurement> fresh)
    {
        foreach (var m in fresh)
        {
            Items.RemoveAll(x => x.Series == m.Series && x.File == m.File);
            Items.Add(m);
        }
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lines = Items.Select(m => string.Join(';', m.Series, m.File, m.Operations,
            m.ReadMs.ToString(CultureInfo.InvariantCulture), m.ExecMs.ToString(CultureInfo.InvariantCulture)));
        File.WriteAllLines(_path, lines.Prepend("Series;File;Operations;ReadMs;ExecMs"));
    }
}
