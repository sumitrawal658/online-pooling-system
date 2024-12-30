namespace PollSystem.Services.Context
{
    public class OperationContext
    {
        private static readonly AsyncLocal<OperationContext> _current = new();
        private readonly Dictionary<string, object> _data;
        private readonly Stack<string> _operationStack;

        public string CorrelationId { get; }
        public string UserId { get; set; }
        public DateTime StartTime { get; }
        public IReadOnlyDictionary<string, object> Data => _data;
        public IEnumerable<string> OperationStack => _operationStack;

        private OperationContext(string correlationId)
        {
            CorrelationId = correlationId;
            StartTime = DateTime.UtcNow;
            _data = new Dictionary<string, object>();
            _operationStack = new Stack<string>();
        }

        public static OperationContext Current
        {
            get => _current.Value ??= new OperationContext(Guid.NewGuid().ToString());
            private set => _current.Value = value;
        }

        public static IDisposable BeginScope(string operationName)
        {
            Current._operationStack.Push(operationName);
            return new OperationScope(operationName);
        }

        public void SetData(string key, object value)
        {
            _data[key] = value;
        }

        public T GetData<T>(string key)
        {
            return _data.TryGetValue(key, out var value) ? (T)value : default;
        }

        public static void Reset()
        {
            Current = new OperationContext(Guid.NewGuid().ToString());
        }

        private class OperationScope : IDisposable
        {
            private readonly string _operationName;
            private bool _disposed;

            public OperationScope(string operationName)
            {
                _operationName = operationName;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    var current = Current._operationStack.Pop();
                    Debug.Assert(current == _operationName, "Operation stack mismatch");
                    _disposed = true;
                }
            }
        }
    }
} 