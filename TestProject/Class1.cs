using System;
using Newtonsoft.Json;
using log4net;

namespace TestProject
{
    public class Class1
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Class1));
        
        public void DoSomething()
        {
            var data = new { Name = "Test", Value = 123 };
            var json = JsonConvert.SerializeObject(data);
            log.Info($"Serialized: {json}");
        }
    }
}