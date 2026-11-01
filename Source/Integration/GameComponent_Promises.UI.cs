using System.Collections.Generic;

namespace ColonyPromises
{
    public readonly struct PromiseButtonStatistics
    {
        public readonly int Requests;
        public readonly int ActivePromises;
        public PromiseButtonStatistics(int requests, int activePromises)
        { Requests = requests; ActivePromises = activePromises; }
    }

    public sealed partial class GameComponent_Promises
    {
        public long ButtonIndexStamp => ((long)open.Count << 32) | (uint)active.Count;
        public PromiseButtonStatistics ButtonStatistics(int now)
        {
            int requests = 0;
            foreach (var state in VisibleOpenRequests(now)) requests++;
            var promises = new HashSet<PromiseRecord>();
            foreach (var state in active.Values)
                if (state.Active != null) promises.Add(state.Active);
            return new PromiseButtonStatistics(requests, promises.Count);
        }
    }
}
