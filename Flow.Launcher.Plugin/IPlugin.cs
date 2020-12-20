using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin
{
    public interface IPlugin
    {
        List<Result> Query(Query query)
        {
            return new List<Result>();
        }
        Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            return Task.Run(() => Query(query));
        }
        void Init(PluginInitContext context);
    }
}