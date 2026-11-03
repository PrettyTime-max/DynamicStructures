using System.Text;
using DynamicStructures.Core;

namespace DynamicStructures.Tests;

public class CoreTests
{
    [Fact]
    public void Stack_IsLifo()
    {
        var stack = new LinkedStack<string>();
        Assert.True(stack.IsEmpty());
        stack.Push("56"); stack.Push("7"); stack.Push("cat");
        Assert.Equal("cat 7 56", stack.Print());
        Assert.Equal("cat", stack.Pop());
        Assert.Equal("7", stack.Top());
        Assert.Throws<InvalidOperationException>(() => new LinkedStack<int>().Pop());
    }

    [Theory]
    [InlineData(typeof(ListQueue<string>))]
    [InlineData(typeof(StandardQueue<string>))]
    public void Queue_IsFifo(Type type)
    {
        var queue = (IQueue<string>)Activator.CreateInstance(type)!;
        queue.Enqueue("56"); queue.Enqueue("7"); queue.Enqueue("cat");
        Assert.Equal("56", queue.Dequeue());
        Assert.Equal("7", queue.Peek());
        Assert.Equal("7 cat", queue.Print());
    }

    [Fact]
    public void Parse_ExampleFromAssignment()
    {
        var ops = Runner.Parse("3 4 1,56 1,7 1,cat 2 5 4");
        Assert.Equal(8, ops.Length);
        Assert.Equal(new Operation(1, "cat"), ops[4]);
    }

    [Theory]
    [InlineData("3 4 1", "Операция №3 «1»")]
    [InlineData("1, 2", "Операция №1 «1,»")]
    [InlineData("1,5 7", "Операция №2 «7»")]
    public void Parse_ReportsErrorPosition(string text, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => Runner.Parse(text));
        Assert.StartsWith(expected, ex.Message);
    }

    [Fact]
    public void Measure_LogsEveryOperation()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "3 4 1,56 1,7 1,cat 2 5 4");
        var log = new StringBuilder();
        var results = Runner.Measure([path], Runner.Queues, log, null, CancellationToken.None);
        File.Delete(path);

        Assert.Equal(2, results.Count);
        var text = log.ToString();
        Assert.Contains("1. Начало() → пусто", text);
        Assert.Contains("6. Удаление() → 56", text);
        Assert.Contains("7. Печать() → 7 cat", text);
        Assert.Contains("8. Пуста?() → нет", text);
    }

    [Theory]
    [InlineData("2+2", "2 2 +")]
    [InlineData("(7+8)/(3+2)", "7 8 + 3 2 + /")]
    [InlineData("2^3^2", "2 3 2 ^ ^")]
    [InlineData("-2^2", "2 2 ^ neg")]
    [InlineData("sqrt(16)*2 : 4", "16 sqrt 2 * 4 :")]
    [InlineData("sin 0 + ln(1)", "0 sin 1 ln +")]
    public void InfixToPostfix(string infix, string postfix) =>
        Assert.Equal(postfix, Expressions.InfixToPostfix(infix));

    [Theory]
    [InlineData("2+")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("2 3")]
    [InlineData("foo(2)")]
    public void InfixToPostfix_RejectsInvalid(string infix) =>
        Assert.Throws<FormatException>(() => Expressions.InfixToPostfix(infix));

    [Theory]
    [InlineData("7 8 + 3 2 + /", 3)]
    [InlineData("9 3 : 2 ^", 9)]
    [InlineData("2 3 2 ^ ^", 512)]
    [InlineData("1 ln 0 cos +", 1)]
    [InlineData("-5 neg 16 sqrt *", 20)]
    public void EvaluatePostfix(string postfix, double expected) =>
        Assert.Equal(expected, Expressions.EvaluatePostfix(postfix).Value, 10);

    [Theory]
    [InlineData("1 0 /")]
    [InlineData("0 ln")]
    [InlineData("-1 sqrt")]
    [InlineData("1 +")]
    [InlineData("1 2")]
    [InlineData("1 2 )")]
    public void EvaluatePostfix_RejectsInvalid(string postfix) =>
        Assert.ThrowsAny<Exception>(() => Expressions.EvaluatePostfix(postfix));

    [Fact]
    public void ListAlgorithms()
    {
        var list = new SinglyLinkedList<int>([1, 2, 2, 3, 4, 4, 5]);
        Assert.Equal(5, list.CountDistinct());

        list.Reverse();
        Assert.Equal("5 4 4 3 2 2 1", list.ToString());
        list.MoveLastToFront();
        Assert.Equal("1 5 4 4 3 2 2", list.ToString());
        list.MoveFirstToBack();
        Assert.Equal("5 4 4 3 2 2 1", list.ToString());

        list.RemoveNonUnique();
        Assert.Equal("5 3 1", list.ToString());
        Assert.Equal(3, list.Count);
        list.AddLast(9);
        Assert.Equal("5 3 1 9", list.ToString());
    }

    [Fact]
    public void RemoveNonUnique_CanEmptyList()
    {
        var list = new SinglyLinkedList<int>([2, 2, 2]);
        list.RemoveNonUnique();
        Assert.True(list.IsEmpty);
        list.AddLast(1);
        Assert.Equal("1", list.ToString());
    }
}
