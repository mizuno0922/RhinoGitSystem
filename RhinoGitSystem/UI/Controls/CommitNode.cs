using System;
using System.Collections.Generic;
using Eto.Drawing;
using RhinoGitSystem.Models;

namespace RhinoGitSystem.UI.Controls
{
    public class CommitNode
    {
        public string CommitId { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTime Timestamp { get; set; }
        public string BranchName { get; set; }
        public string ParentCommit { get; set; }
        public Point Position { get; set; }
        public Color Color { get; set; }
        public List<ObjectChange> Changes { get; set; }
        public PointF? BranchPoint { get; set; }
        public bool IsMergePoint { get; set; }
        public CommitNode MergeSourceNode { get; set; }
        public string ParentBranch { get; set; }
    }
}
