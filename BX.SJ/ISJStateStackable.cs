namespace BX.SJ
{
    /// <summary>
    /// Used for any SJ related object with stackable opaque state.
    /// </summary>
    public interface ISJStateStackable
    {
        int MaxStateCount { get; set; }
        int PushState();
        int PopState();
    }
}
