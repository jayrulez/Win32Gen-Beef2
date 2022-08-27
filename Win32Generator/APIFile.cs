using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Win32Generator
{
    public class APIFile
    {
        public string Name { get; }
        public string InputPath { get; }
        public string OutputPath { get; }
        public string Api { get; }

        public HashSet<string> Dependencies { get; } = new HashSet<string>();

        public JObject Content {get; set;}

        public APIFile(string name, string api, string inputPath, string outputPath)
        {
            Name = name;
            Api = api;
            InputPath = inputPath;
            OutputPath = outputPath;
        }

        public void AddDependency(string inputPath)
        {
            if (inputPath != InputPath)
                Dependencies.Add(inputPath);
        }
    }
}
