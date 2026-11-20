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

            if (!popIfError || reader.HasError)
            {
                reader.PopState();
            }
            disposed = true;
        }
    }

    /// <summary>
    /// Used for any SJ related object with stackable opaque state.
    /// <br>It is useful to start things like transactions.</br>
    /// </summary>
    public interface ISJStateStackable
    {
        bool HasError { get; }
        int MaxStateCount { get; set; }
        int PushState();
        int PopState();
        /// <param name="popIfError">Whether to pop only if <see cref="HasError"/>.
        /// If left <see langword="false"/>, it will pop state regardless of when disposed.</param>
        StateStackScope UseState(bool popIfError = false) => new StateStackScope(this, popIfError);
    }
}
