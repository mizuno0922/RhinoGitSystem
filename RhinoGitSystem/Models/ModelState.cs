using System;
using System.Collections.Generic;

namespace RhinoGitSystem.Models
{
    public class ModelState
    {
        public string CommitId { get; set; }
        public List<ObjectChange> Changes { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string BranchName { get; set; }
        public string ParentCommit { get; set; }
        public string Author { get; set; }
    }
}
