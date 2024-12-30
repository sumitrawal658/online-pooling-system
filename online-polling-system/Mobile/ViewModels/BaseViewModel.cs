using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PollSystem.Mobile.ViewModels
{
    public abstract partial class BaseViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        protected readonly List<IDisposable> _disposables = new();

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var disposable in _disposables)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error disposing {disposable.GetType().Name}: {ex.Message}");
                        }
                    }
                    _disposables.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void AddDisposable(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }
    }
} 