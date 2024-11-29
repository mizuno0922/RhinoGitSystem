using System.Collections.Generic;

namespace RhinoGitSystem.Models
{
    public class Branch
    {
        public string Name { get; set; }
        public List<string> Commits { get; set; }
    }
}