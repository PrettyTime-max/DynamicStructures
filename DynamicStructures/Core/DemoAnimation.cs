namespace DynamicStructures.Core;

public abstract record DemoFrame(string Note);

public sealed record HashBucketItem(string Key, string Value);

public sealed record HashTableFrame(
    string Note,
    IReadOnlyList<IReadOnlyList<HashBucketItem>> Buckets,
    int ActiveBucket = -1,
    string? ActiveKey = null,
    bool IsDelete = false) : DemoFrame(Note);

public sealed record PrintQueueFrame(
    string Note,
    string? CurrentJob,
    int Printed,
    int TotalPages,
    int Clock,
    IReadOnlyList<IReadOnlyList<string>> Queues,
    IReadOnlyList<string> Done,
    string? HighlightJob = null,
    bool IsCancel = false) : DemoFrame(Note);

public sealed record HeapFrame(
    string Note,
    IReadOnlyList<int> Heap,
    IReadOnlyList<int?> Sorted,
    int FocusA = -1,
    int FocusB = -1,
    bool IsSwap = false) : DemoFrame(Note);

public sealed record QueueFrame(
    string Note,
    IReadOnlyList<string> Items,
    int HighlightIndex = -1,
    bool IsRemove = false,
    string ImplTitle = "Queue<T>",
    int StepsCount = 0,
    bool IsShift = false) : DemoFrame(Note);
