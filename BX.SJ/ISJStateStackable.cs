using System;

namespace BX.SJ
{
    public struct StateStackScope : IDisposable
    {
        private bool disposed;
        private readonly bool popIfError;
        private readonly ISJStateStackable reader;

        public StateStackScope(ISJStateStackable reader, bool popIfError)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

            this.popIfError = popIfError;
            disposed = reader.PushState() < 0;
        }

        public void Dispose()
        {
            if (disposed || reader == null) return;

            if (!popIfError || !string.IsNullOrEmpty(reader.Error))
            {
                reader.PopState();
            }
            disposed = true;
        }
    }

    /// <summary>
    /// Used for any SJ related object with stackable opaque state.
    /// </summary>
    public interface ISJStateStackable
    {
        string Error { get; }            // Error contained in the stated object. Is not necessary, can be left null for most implementations
        int MaxStateCount { get; set; }
        int PushState();
        int PopState();
        StateStackScope UseState(bool popIfError = false) => new StateStackScope(this, popIfError);
    }
}
