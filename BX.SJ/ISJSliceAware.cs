 namespace BX.SJ
{
    /// <summary>
    /// Opt-in hints for any <see cref="SJReader"/> data source for a 
    /// region that will be lexed into the relevant slice of data that can be evaluated later.
    /// <br>* Running out of region size shouldn't be an error if the data source can be freely seeked.
    /// In fact, the data should be freely seekable. This interface is justifiable if the data is 
    /// evaluated as it is being read (which is the case with most code using BXSJ)</br>
    /// <br>* This is mainly meant to be an optimization, if for example the file 
    /// has a different encoding or to not seek the data again. It is not required for being usable as a data source.</br>
    /// <br>* When <see cref="BeginSliceableRegion"/> and <see cref="EndSliceableRegion"/> is called,
    /// include the "<see cref="System.Collections.Generic.IEnumerator{T}.Current"/>" data.</br>
    /// </summary>
    public interface ISJSliceAware
    {
        int MaxRegionSize { get; }
        void BeginSliceableRegion();
        void EndSliceableRegion();
    }
}
