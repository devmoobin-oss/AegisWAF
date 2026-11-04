using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: XunitInProcRunner <class-name> [<class-name> ...]");
    return 2;
}

var targetClasses = args.ToHashSet(StringComparer.Ordinal);
var assemblyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "AegisWAF.Tests.dll"));
var options = new Options();
var sink = new ExecutionSink(targetClasses);
var executor = new Executor(AssemblyName.GetAssemblyName(assemblyPath), new SourceInformationProvider(), sink);
var discoverySink = new DiscoverySink();
executor.Discover(discoverySink, options);
if (!discoverySink.Completed.Wait(TimeSpan.FromSeconds(30)))
{
    Console.Error.WriteLine("Timed out waiting for in-process xUnit discovery.");
    return 3;
}
var targeted = discoverySink.TestCases
    .Where(testCase => targetClasses.Contains(testCase.TestMethod.TestClass.Class.Name.Split('.').Last()))
    .ToArray();

var discoveredClasses = targeted
    .Select(testCase => testCase.TestMethod.TestClass.Class.Name.Split('.').Last())
    .ToHashSet(StringComparer.Ordinal);
Console.WriteLine($"DISCOVERED targeted={targeted.Length} classes={string.Join(',', discoveredClasses.Order())}");
executor.Run(targeted, sink, options);
if (!sink.Completed.Wait(TimeSpan.FromSeconds(120)))
{
    Console.Error.WriteLine("Timed out waiting for in-process xUnit execution.");
    return 3;
}

var missingClasses = targetClasses.Except(discoveredClasses, StringComparer.Ordinal).ToArray();
if (missingClasses.Length > 0 || sink.TargetedFailed != 0 || sink.TargetedPassed != targeted.Length)
{
    Console.Error.WriteLine($"TARGET_SUMMARY passed={sink.TargetedPassed} failed={sink.TargetedFailed} expected={targeted.Length} missing={string.Join(',', missingClasses)}");
    return 1;
}

Console.WriteLine($"TARGET_SUMMARY passed={sink.TargetedPassed} failed={sink.TargetedFailed} expected={targeted.Length}");
return 0;

sealed class Executor : XunitTestFrameworkExecutor
{
    public Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) { }

    public void Discover(IMessageSink sink, ITestFrameworkDiscoveryOptions options) => CreateDiscoverer().Find(false, sink, options);

    public void Run(IEnumerable<IXunitTestCase> testCases, IMessageSink sink, ITestFrameworkExecutionOptions options) => RunTestCases(testCases, sink, options);
}

sealed class Options : ITestFrameworkDiscoveryOptions, ITestFrameworkExecutionOptions
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    public TValue GetValue<TValue>(string name) => _values.TryGetValue(name, out var value) && value is TValue typed ? typed : default!;
    public void SetValue<TValue>(string name, TValue value) => _values[name] = value;
}

sealed class SourceInformationProvider : ISourceInformationProvider
{
    public ISourceInformation? GetSourceInformation(ITestCase testCase) => null;
    public void Dispose() { }
}

sealed class DiscoverySink : IMessageSink
{
    public List<IXunitTestCase> TestCases { get; } = new();
    public ManualResetEventSlim Completed { get; } = new(false);

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is TestCaseDiscoveryMessage discovered)
            TestCases.Add((IXunitTestCase)discovered.TestCase);
        else if (message is DiscoveryCompleteMessage)
            Completed.Set();
        return true;
    }
}

sealed class ExecutionSink : IMessageSink
{
    private readonly HashSet<string> _targetClasses;
    private int _targetedPassed;
    private int _targetedFailed;

    public ExecutionSink(HashSet<string> targetClasses) => _targetClasses = targetClasses;
    public ManualResetEventSlim Completed { get; } = new(false);
    public int TargetedPassed => Volatile.Read(ref _targetedPassed);
    public int TargetedFailed => Volatile.Read(ref _targetedFailed);

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is TestFailed failed && IsTargeted(failed.TestMethod.TestClass.Class.Name))
        {
            Interlocked.Increment(ref _targetedFailed);
            var detail = failed.StackTraces.FirstOrDefault();
            Console.Error.WriteLine($"TARGET_FAIL {failed.TestMethod.TestClass.Class.Name}.{failed.TestMethod.Method.Name}: {failed.Messages.FirstOrDefault()}\n{detail}");
        }
        else if (message is TestPassed passed && IsTargeted(passed.TestMethod.TestClass.Class.Name))
        {
            Interlocked.Increment(ref _targetedPassed);
            Console.WriteLine($"TARGET_PASS {passed.TestMethod.TestClass.Class.Name}.{passed.TestMethod.Method.Name}");
        }
        else if (message is ITestAssemblyFinished finished)
        {
            Console.WriteLine($"ASSEMBLY total={finished.TestsRun} failed={finished.TestsFailed} skipped={finished.TestsSkipped}");
            Completed.Set();
        }
        return true;
    }

    private bool IsTargeted(string className) => _targetClasses.Contains(className.Split('.').Last());
}
