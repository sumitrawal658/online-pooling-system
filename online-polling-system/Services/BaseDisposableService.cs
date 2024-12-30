namespace PollSystem.Services
{
    public abstract class BaseDisposableService : IDisposable, IAsyncDisposable
    {
        private bool _disposed;
        protected readonly ILogger _logger;

        protected BaseDisposableService(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    DisposeManagedResources();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing managed resources");
                }
            }

            try
            {
                DisposeUnmanagedResources();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing unmanaged resources");
            }

            _disposed = true;
        }

        protected virtual ValueTask DisposeAsyncCore()
        {
            return ValueTask.CompletedTask;
        }

        protected virtual void DisposeManagedResources() { }
        protected virtual void DisposeUnmanagedResources() { }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        ~BaseDisposableService()
        {
            Dispose(false);
        }
    }
} 