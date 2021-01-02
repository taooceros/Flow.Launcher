using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin
{
    public interface IPlugin
    {
        List<Result> Query(Query query);
        
        void Init(PluginInitContext context);
    }
}