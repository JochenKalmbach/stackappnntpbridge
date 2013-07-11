using System.Collections.Generic;

namespace Stacky
{
    public class PagedList<T> : IPagedList<T>
    {
        //public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public bool HasMore { get; set; }
        public int BackOff { get; set; }
        public int QuotaMax { get; set; }
        public int QuotaRemaining { get; set; }

        public PagedList(IEnumerable<T> items)
        {
            Items = new List<T>(items);
        }

        public PagedList(IEnumerable<T> items, Response response)
            : this(items)
        {
          //TotalItems = response.Total;
          CurrentPage = response.CurrentPage;
          PageSize = response.PageSize;
          HasMore = response.HasMore;
          BackOff = response.BackOff;
          QuotaMax = response.QuotaMax;
          QuotaRemaining = response.QuotaRemaining;
        }

        protected List<T> Items { get; set; }

        public IEnumerator<T> GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return ((System.Collections.IEnumerable)Items).GetEnumerator();
        }
    }
}