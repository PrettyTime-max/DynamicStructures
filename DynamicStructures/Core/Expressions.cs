using System.IO;
using System.Globalization;
using System.Text;

namespace DynamicStructures.Core;

public sealed record Evaluation(double Value, IReadOnlyList<string> Steps, IReadOnlyList<Operation> Script);

/// <summary>Последовательность операций стека для анимации (Operation: 1 — вставка, 2 — удаление, 6 — только пояснение).</summary>
public sealed record AnimationScript(string Title, IReadOnlyList<Operation> Ops);

/// <summary>Перевод инфиксной записи в постфиксную и вычисление постфиксной записи на стеке.</summary>
public static class Expressions
{
    private enum Token { None, Operand, Open, Binary, Prefix }

    private static readonly HashSet<string> Functions = ["ln", "cos", "sin", "sqrt"];
    private const string Negate = "neg";

    private static int Precedence(string op) => op switch
    {
        "+" or "-" => 1,
        "*" or "/" or ":" => 2,
        Negate => 3,
        "^" => 4,
        _ => 5, // функции
    };

    public static string InfixToPostfix(string infix, List<Operation>? script = null)
    {
        var output = new List<string>();
        var ops = new LinkedStack<string>();
        var prev = Token.None;
        var i = 0;

        string Out() => string.Join(" ", output);
        void PushOp(string text, string note) { ops.Push(text); script?.Add(new(1, text, note)); }
        void PopToOutput()
        {
            var top = ops.Pop();
            output.Add(top);
            script?.Add(new(2, null, $"«{top}» из стека в вывод; вывод: {Out()}"));
        }

        while (i < infix.Length)
        {
            var c = infix[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            var start = i;
            if (char.IsDigit(c) || c is '.' or ',')
            {
                while (i < infix.Length && (char.IsDigit(infix[i]) || infix[i] is '.' or ',')) i++;
                var number = infix[start..i].Replace(',', '.');
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw Error(start, $"неверное число «{number}»");
                if (prev == Token.Operand) throw Error(start, "пропущен оператор");
                output.Add(number);
                script?.Add(new(6, null, $"число {number} сразу в вывод; вывод: {Out()}"));
                prev = Token.Operand;
                continue;
            }

            if (char.IsLetter(c))
            {
                while (i < infix.Length && char.IsLetter(infix[i])) i++;
                var name = infix[start..i].ToLowerInvariant();
                if (!Functions.Contains(name)) throw Error(start, $"неизвестная функция «{name}»");
                if (prev == Token.Operand) throw Error(start, "пропущен оператор");
                PushOp(name, $"функция {name} в стек");
                prev = Token.Prefix;
                continue;
            }

            i++;
            switch (c)
            {
                case '(':
                    if (prev == Token.Operand) throw Error(start, "пропущен оператор перед «(»");
                    PushOp("(", "«(» в стек");
                    prev = Token.Open;
                    break;
                case ')':
                    if (prev != Token.Operand) throw Error(start, "пустые скобки или нет операнда перед «)»");
                    while (!ops.IsEmpty() && ops.Top() != "(") PopToOutput();
                    if (ops.IsEmpty()) throw Error(start, "лишняя «)»");
                    ops.Pop();
                    script?.Add(new(2, null, "«(» убираем из стека"));
                    break;
                case '+' or '-' or '*' or '/' or ':' or '^':
                    var op = c.ToString();
                    if (prev != Token.Operand)
                    {
                        if (c == '+') break;
                        if (c != '-') throw Error(start, $"у «{c}» нет левого операнда");
                        PushOp(Negate, "унарный минус в стек");
                        prev = Token.Prefix;
                        break;
                    }
                    while (!ops.IsEmpty() && ops.Top() != "(" && ShouldPop(ops.Top(), op)) PopToOutput();
                    PushOp(op, $"«{op}» в стек");
                    prev = Token.Binary;
                    break;
                default:
                    throw Error(start, $"недопустимый символ «{c}»");
            }
        }

        if (prev != Token.Operand) throw new FormatException("Выражение пустое или не закончено.");
        while (!ops.IsEmpty())
        {
            if (ops.Top() == "(") throw new FormatException("Не закрыта «(».");
            PopToOutput();
        }
        return Out();
    }

    private static bool ShouldPop(string top, string incoming)
    {
        int pTop = Precedence(top), pIn = Precedence(incoming);
        return pTop > pIn || (pTop == pIn && incoming != "^");
    }

    private static FormatException Error(int position, string message) =>
        new($"Позиция {position + 1}: {message}.");

    public static Evaluation EvaluatePostfix(string postfix)
    {
        var tokens = postfix.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("Пустое выражение.");

        var stack = new LinkedStack<double>();
        var steps = new List<string>();
        var script = new List<Operation>();

        foreach (var token in tokens)
        {
            if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                stack.Push(number);
                script.Add(new(1, Format(number), $"число {Format(number)} в стек"));
            }
            else if (token is "+" or "-" or "*" or "/" or ":" or "^")
            {
                if (stack.Count < 2) throw new FormatException($"Для «{token}» не хватает операндов.");
                var b = stack.Pop();
                var a = stack.Pop();
                var result = Check(token, token switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    "^" => Math.Pow(a, b),
                    _ => b == 0 ? throw new ArithmeticException("Деление на ноль.") : a / b,
                });
                stack.Push(result);
                script.Add(new(2, null, $"«{token}»: снимаем второй операнд {Format(b)}"));
                script.Add(new(2, null, $"«{token}»: снимаем первый операнд {Format(a)}"));
                script.Add(new(1, Format(result), $"{Format(a)} {token} {Format(b)} = {Format(result)}, результат в стек"));
            }
            else if (Functions.Contains(token) || token == Negate)
            {
                if (stack.IsEmpty()) throw new FormatException($"Для «{token}» нет операнда.");
                var x = stack.Pop();
                var result = Check(token, token switch
                {
                    "ln" => x <= 0 ? throw new ArithmeticException($"ln({Format(x)}): аргумент должен быть > 0.") : Math.Log(x),
                    "sqrt" => x < 0 ? throw new ArithmeticException($"sqrt({Format(x)}): аргумент должен быть ≥ 0.") : Math.Sqrt(x),
                    "sin" => Math.Sin(x),
                    "cos" => Math.Cos(x),
                    _ => -x,
                });
                stack.Push(result);
                script.Add(new(2, null, $"«{token}»: снимаем операнд {Format(x)}"));
                script.Add(new(1, Format(result), $"{token}({Format(x)}) = {Format(result)}, результат в стек"));
            }
            else
            {
                throw new FormatException($"Недопустимый элемент «{token}».");
            }

            steps.Add($"{token,-6} │ стек (вершина слева): {StackToString(stack)}");
        }

        if (stack.Count != 1) throw new FormatException($"Лишние операнды: в стеке осталось {stack.Count} значения.");
        return new Evaluation(stack.Pop(), steps, script);
    }

    private static double Check(string token, double value) =>
        double.IsFinite(value) ? value : throw new ArithmeticException($"«{token}»: результат вне диапазона.");

    private static string StackToString(LinkedStack<double> stack)
    {
        var copy = new List<double>(stack.Count);
        var temp = new LinkedStack<double>();
        while (!stack.IsEmpty()) { var v = stack.Pop(); copy.Add(v); temp.Push(v); }
        while (!temp.IsEmpty()) stack.Push(temp.Pop());
        return string.Join(" ", copy.Select(Format));
    }

    public static string Format(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>Вычисляет каждую непустую строку файла как постфиксное выражение.</summary>
    public static string EvaluateFile(string path, List<AnimationScript>? scripts = null, int onlyLine = 0) => ProcessFile(path, scripts, onlyLine, line =>
    {
        var evaluation = EvaluatePostfix(line);
        return (Describe(evaluation), evaluation.Script, Format(evaluation.Value));
    });

    /// <summary>Переводит каждую непустую строку файла из инфиксной записи в постфиксную и вычисляет её.</summary>
    public static string ConvertFile(string path, List<AnimationScript>? scripts = null, int onlyLine = 0) => ProcessFile(path, scripts, onlyLine, line =>
    {
        var script = new List<Operation>();
        var postfix = InfixToPostfix(line, script);
        var value = Format(EvaluatePostfix(postfix).Value);
        return ($"Постфиксная запись: {postfix}{Environment.NewLine}Результат: {value}{Environment.NewLine}", script, $"{postfix}  =  {value}");
    });

    /// <summary>Непустые строки файла с их номерами (с единицы) — для выбора конкретного выражения.</summary>
    public static IReadOnlyList<(int Line, string Text)> ReadLines(string path) =>
        File.ReadAllLines(path).Select((text, i) => (Line: i + 1, Text: text.Trim())).Where(l => l.Text.Length > 0).ToList();

    /// <summary>
    /// Обрабатывает строки по одной (или только строку <paramref name="onlyLine"/>): ошибка в строке попадает в отчёт,
    /// остальные строки обрабатываются дальше. Сначала идёт итог, потом подробности.
    /// </summary>
    private static string ProcessFile(string path, List<AnimationScript>? scripts, int onlyLine,
        Func<string, (string Text, IReadOnlyList<Operation> Script, string Short)> handle)
    {
        var summary = new StringBuilder();
        var details = new StringBuilder();
        var total = 0;
        var errors = 0;
        foreach (var (number, line) in ReadLines(path))
        {
            if (onlyLine > 0 && number != onlyLine) continue;
            total++;
            details.AppendLine($"Строка {number}: {line}");
            try
            {
                var (text, script, brief) = handle(line);
                details.AppendLine(text);
                summary.AppendLine($"  {number}: {line}  →  {brief}");
                scripts?.Add(new(line, script));
            }
            catch (Exception ex) when (ex is FormatException or ArithmeticException)
            {
                errors++;
                details.AppendLine($"  Ошибка: {ex.Message}").AppendLine();
                summary.AppendLine($"  {number}: {line}  →  ошибка: {ex.Message}");
            }
        }
        if (total == 0) throw new FormatException(onlyLine > 0 ? $"Строка {onlyLine} пуста или не найдена." : "Файл пуст.");
        var head = errors == 0 ? $"Обработано строк: {total}, ошибок нет." : $"Обработано строк: {total}, с ошибками: {errors}.";
        return $"=== Итог ==={Environment.NewLine}{head}{Environment.NewLine}{summary}{Environment.NewLine}=== Подробно ==={Environment.NewLine}{details}";
    }

    public static string Describe(Evaluation evaluation) =>
        string.Join(Environment.NewLine, evaluation.Steps) + Environment.NewLine +
        $"Результат: {Format(evaluation.Value)}" + Environment.NewLine;
}
