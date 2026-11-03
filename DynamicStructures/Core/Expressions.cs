using System.IO;
using System.Globalization;
using System.Text;

namespace DynamicStructures.Core;

public sealed record Evaluation(double Value, IReadOnlyList<string> Steps);

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

    public static string InfixToPostfix(string infix)
    {
        var output = new List<string>();
        var ops = new LinkedStack<string>();
        var prev = Token.None;
        var i = 0;

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
                prev = Token.Operand;
                continue;
            }

            if (char.IsLetter(c))
            {
                while (i < infix.Length && char.IsLetter(infix[i])) i++;
                var name = infix[start..i].ToLowerInvariant();
                if (!Functions.Contains(name)) throw Error(start, $"неизвестная функция «{name}»");
                if (prev == Token.Operand) throw Error(start, "пропущен оператор");
                ops.Push(name);
                prev = Token.Prefix;
                continue;
            }

            i++;
            switch (c)
            {
                case '(':
                    if (prev == Token.Operand) throw Error(start, "пропущен оператор перед «(»");
                    ops.Push("(");
                    prev = Token.Open;
                    break;
                case ')':
                    if (prev != Token.Operand) throw Error(start, "пустые скобки или нет операнда перед «)»");
                    while (!ops.IsEmpty() && ops.Top() != "(") output.Add(ops.Pop());
                    if (ops.IsEmpty()) throw Error(start, "лишняя «)»");
                    ops.Pop();
                    break;
                case '+' or '-' or '*' or '/' or ':' or '^':
                    var op = c.ToString();
                    if (prev != Token.Operand)
                    {
                        if (c == '+') break;
                        if (c != '-') throw Error(start, $"у «{c}» нет левого операнда");
                        ops.Push(Negate);
                        prev = Token.Prefix;
                        break;
                    }
                    while (!ops.IsEmpty() && ops.Top() != "(" && ShouldPop(ops.Top(), op)) output.Add(ops.Pop());
                    ops.Push(op);
                    prev = Token.Binary;
                    break;
                default:
                    throw Error(start, $"недопустимый символ «{c}»");
            }
        }

        if (prev != Token.Operand) throw new FormatException("Выражение пустое или не закончено.");
        while (!ops.IsEmpty())
        {
            var op = ops.Pop();
            if (op == "(") throw new FormatException("Не закрыта «(».");
            output.Add(op);
        }
        return string.Join(" ", output);
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

        foreach (var token in tokens)
        {
            if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                stack.Push(number);
            }
            else if (token is "+" or "-" or "*" or "/" or ":" or "^")
            {
                if (stack.Count < 2) throw new FormatException($"Для «{token}» не хватает операндов.");
                var b = stack.Pop();
                var a = stack.Pop();
                stack.Push(Check(token, token switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    "^" => Math.Pow(a, b),
                    _ => b == 0 ? throw new ArithmeticException("Деление на ноль.") : a / b,
                }));
            }
            else if (Functions.Contains(token) || token == Negate)
            {
                if (stack.IsEmpty()) throw new FormatException($"Для «{token}» нет операнда.");
                var x = stack.Pop();
                stack.Push(Check(token, token switch
                {
                    "ln" => x <= 0 ? throw new ArithmeticException($"ln({Format(x)}): аргумент должен быть > 0.") : Math.Log(x),
                    "sqrt" => x < 0 ? throw new ArithmeticException($"sqrt({Format(x)}): аргумент должен быть ≥ 0.") : Math.Sqrt(x),
                    "sin" => Math.Sin(x),
                    "cos" => Math.Cos(x),
                    _ => -x,
                }));
            }
            else
            {
                throw new FormatException($"Недопустимый элемент «{token}».");
            }

            steps.Add($"{token,-6} │ стек (вершина слева): {StackToString(stack)}");
        }

        if (stack.Count != 1) throw new FormatException($"Лишние операнды: в стеке осталось {stack.Count} значения.");
        return new Evaluation(stack.Pop(), steps);
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
    public static string EvaluateFile(string path)
    {
        var report = new StringBuilder();
        var lines = File.ReadAllLines(path);
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            if (line.Length == 0) continue;
            try
            {
                report.AppendLine($"Строка {n + 1}: {line}").AppendLine(Describe(EvaluatePostfix(line)));
            }
            catch (Exception ex) when (ex is FormatException or ArithmeticException)
            {
                throw new FormatException($"Строка {n + 1} («{line}»): {ex.Message}", ex);
            }
        }
        return report.Length == 0 ? throw new FormatException("Файл пуст.") : report.ToString();
    }

    public static string Describe(Evaluation evaluation) =>
        string.Join(Environment.NewLine, evaluation.Steps) + Environment.NewLine +
        $"Результат: {Format(evaluation.Value)}" + Environment.NewLine;
}
