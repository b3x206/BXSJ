namespace BX.SJ
{
    public interface ISJStateStackable
    {
        int MaxStateCount { get; set; }
        int PushState();
        int PopState();
    }
}
